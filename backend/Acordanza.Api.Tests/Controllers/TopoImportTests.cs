using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Acordanza.Api.Authorization;
using Acordanza.Api.Tests.Infrastructure;
using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Acordanza.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// Plan 30 §1. Only the HTTP surface lives here — TopoImportsController is an Api concern,
// so it's exercised through real requests against the shared fixture. Everything the
// worker does with the row afterwards (gate, extraction, match-or-create, blob promotion)
// belongs to Acordanza.Worker.Tests, which needs no Api host at all.
public class TopoImportTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly byte[] ImageBytes = [0xFF, 0xD8, 0xFF, 0xD9];

    private static MultipartFormDataContent Upload(
        int areaId, byte[]? bytes = null, string contentType = "image/jpeg", string fileName = "topo.jpg")
    {
        var file = new ByteArrayContent(bytes ?? ImageBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        return new MultipartFormDataContent
        {
            { file, "file", fileName },
            { new StringContent(areaId.ToString()), "areaId" }
        };
    }

    private async Task<Area> SeedAreaAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
    }

    // Keyed, so it can't be confused with the unkeyed route-enrichment client — see
    // TopoImportKeys.QueueServiceKey.
    private Azure.Storage.Queues.QueueClient TopoImportQueueClient =>
        Factory.Services.GetRequiredKeyedService<Azure.Storage.Queues.QueueClient>(TopoImportKeys.QueueServiceKey);

    private async Task<int> QueueLengthAsync()
    {
        var props = await TopoImportQueueClient.GetPropertiesAsync(TestContext.Current.CancellationToken);
        return props.Value.ApproximateMessagesCount;
    }

    // The whole point of gating on route.create rather than route.enrich: someone trusted
    // to create a route by typing it is trusted to create one by photographing it. A plain
    // user has route.create, so a plain user can do this.
    [Fact]
    public async Task Upload_as_a_plain_user_returns_202_and_enqueues_the_row_id()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();

        using var content = Upload(area.Id);
        var response = await user.Client.PostAsync("/api/topo-imports", content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var import = await db.TopoImports.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        import.Status.ShouldBe(TopoImportStatus.Pending);
        import.AreaId.ShouldBe(area.Id);
        import.UploadedByUserId.ShouldBe(user.UserId);
        import.RouteId.ShouldBeNull();
        // Staged under the sweep-able prefix, not the route's own — there is no route yet.
        import.BlobName.ShouldStartWith(TopoImportKeys.StagingPrefix);

        (await QueueLengthAsync()).ShouldBe(1);

        // The message carries the row id and nothing else: everything the worker needs is
        // already on the row, and a queue message is the wrong place to duplicate it.
        var messages = await TopoImportQueueClient.ReceiveMessagesAsync(
            1, cancellationToken: TestContext.Current.CancellationToken);
        var body = JsonSerializer.Deserialize<TopoImportMessage>(messages.Value[0].MessageText);
        body!.ImportId.ShouldBe(import.Id);
    }

    [Fact]
    public async Task An_unsupported_content_type_400s_in_spanish_and_enqueues_nothing()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();

        using var content = Upload(area.Id, contentType: "application/pdf", fileName: "topo.pdf");
        var response = await user.Client.PostAsync("/api/topo-imports", content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe("el formato de la imagen no es válido (usa JPEG, PNG o WebP)");

        await AssertNothingHappenedAsync();
    }

    [Fact]
    public async Task An_oversize_image_400s_and_enqueues_nothing()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();

        var tooBig = new byte[MediaUploadService.MaxImageBytes + 1];
        using var content = Upload(area.Id, tooBig);
        var response = await user.Client.PostAsync("/api/topo-imports", content, TestContext.Current.CancellationToken);

        // RequestSizeLimit rejects it at the pipeline (413) before the action's own 400
        // ever runs; either way the contract that matters is "not accepted, nothing queued".
        response.IsSuccessStatusCode.ShouldBeFalse();
        await AssertNothingHappenedAsync();
    }

    [Fact]
    public async Task A_missing_file_400s_and_enqueues_nothing()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();

        using var content = new MultipartFormDataContent { { new StringContent(area.Id.ToString()), "areaId" } };
        var response = await user.Client.PostAsync("/api/topo-imports", content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe("la imagen es obligatoria");

        await AssertNothingHappenedAsync();
    }

    [Fact]
    public async Task An_unknown_area_400s_and_enqueues_nothing()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);

        using var content = Upload(999999);
        var response = await user.Client.PostAsync("/api/topo-imports", content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe("el sector no existe");

        await AssertNothingHappenedAsync();
    }

    // Scoped by GetUserId, never by an id from the client — the same shape
    // ContributionsController uses, which is why there's no id to forge in the path.
    [Fact]
    public async Task Mine_returns_only_the_callers_own_rows()
    {
        using var mine = await TestUsers.CreateAsync(Factory, Roles.User);
        using var theirs = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();

        using (var content = Upload(area.Id, fileName: "mine.jpg"))
        {
            await mine.Client.PostAsync("/api/topo-imports", content, TestContext.Current.CancellationToken);
        }
        using (var content = Upload(area.Id, fileName: "theirs.jpg"))
        {
            await theirs.Client.PostAsync("/api/topo-imports", content, TestContext.Current.CancellationToken);
        }

        var rows = await mine.Client.GetFromJsonAsync<List<TopoImportRow>>(
            "/api/topo-imports/mine", TestContext.Current.CancellationToken);

        rows!.Count.ShouldBe(1);
        rows[0].FileName.ShouldBe("mine.jpg");
        rows[0].Status.ShouldBe("pending");
        rows[0].AreaNamePath.ShouldBe(area.Name);
    }

    // Status reaches the frontend lowercased, like every other enum it narrows to a
    // literal union (see api/types.ts's TopoImportStatus).
    [Fact]
    public async Task Mine_reports_a_decided_rows_route_and_outcome()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var route = await SeedData.CreateRouteAsync(db, area, user.UserId, "La Loca");
            db.TopoImports.Add(new TopoImport
            {
                BlobName = "pena/la-loca-01.jpg",
                ContentType = "image/jpeg",
                FileName = "topo.jpg",
                AreaId = area.Id,
                UploadedByUserId = user.UserId,
                Status = TopoImportStatus.Completed,
                Outcome = "Vía creada y pendiente de validación",
                RouteId = route.Id,
                RouteWasCreated = true,
                ProcessedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var rows = await user.Client.GetFromJsonAsync<List<TopoImportRow>>(
            "/api/topo-imports/mine", TestContext.Current.CancellationToken);

        var row = rows!.Single();
        row.Status.ShouldBe("completed");
        row.Outcome.ShouldBe("Vía creada y pendiente de validación");
        row.RouteName.ShouldBe("La Loca");
        row.RouteWasCreated.ShouldBeTrue();
        // Both halves of the shareable route URL, so the page links straight to what the
        // import produced without resolving anything itself.
        row.RouteSlug.ShouldNotBeNullOrEmpty();
        row.RouteAreaSlugPath.ShouldNotBeNullOrEmpty();
    }

    private async Task AssertNothingHappenedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.TopoImports.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        (await QueueLengthAsync()).ShouldBe(0);
    }

    private sealed record ErrorResponse(string Error);

    private sealed record TopoImportRow(
        int Id, string FileName, int AreaId, string AreaNamePath, DateTimeOffset CreatedAt,
        string Status, string? Outcome, int? RouteId, string? RouteName, string? RouteSlug,
        string? RouteAreaSlugPath, bool RouteWasCreated, DateTimeOffset? ProcessedAt);
}
