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

// Plans 30 §1 and 31 §1. Only the HTTP surface lives here — RouteImportsController is an
// Api concern, so it's exercised through real requests against the shared fixture. One
// sub-path per kind, one shared list. Everything the worker
// does with the row afterwards (reading it, match-or-create, blob promotion) belongs to
// Acordanza.Worker.Tests, which needs no Api host at all.
public class RouteImportTests(ApiFixture fixture) : IntegrationTestBase(fixture)
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
    // RouteImportKeys.QueueServiceKey.
    private Azure.Storage.Queues.QueueClient RouteImportQueueClient =>
        Factory.Services.GetRequiredKeyedService<Azure.Storage.Queues.QueueClient>(RouteImportKeys.QueueServiceKey);

    private async Task<int> QueueLengthAsync()
    {
        var props = await RouteImportQueueClient.GetPropertiesAsync(TestContext.Current.CancellationToken);
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
        var response = await user.Client.PostAsync("/api/route-imports/image", content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var import = await db.RouteImports.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        import.Status.ShouldBe(RouteImportStatus.Pending);
        import.AreaId.ShouldBe(area.Id);
        import.SubmittedByUserId.ShouldBe(user.UserId);
        import.RouteId.ShouldBeNull();
        // Staged under the sweep-able prefix, not the route's own — there is no route yet.
        import.BlobName.ShouldStartWith(RouteImportKeys.StagingPrefix);

        (await QueueLengthAsync()).ShouldBe(1);

        // The message carries the row id and nothing else: everything the worker needs is
        // already on the row, and a queue message is the wrong place to duplicate it.
        var messages = await RouteImportQueueClient.ReceiveMessagesAsync(
            1, cancellationToken: TestContext.Current.CancellationToken);
        var body = JsonSerializer.Deserialize<RouteImportMessage>(messages.Value[0].MessageText);
        body!.ImportId.ShouldBe(import.Id);
    }

    [Fact]
    public async Task An_unsupported_content_type_400s_in_spanish_and_enqueues_nothing()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();

        using var content = Upload(area.Id, contentType: "application/pdf", fileName: "topo.pdf");
        var response = await user.Client.PostAsync("/api/route-imports/image", content, TestContext.Current.CancellationToken);

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
        var response = await user.Client.PostAsync("/api/route-imports/image", content, TestContext.Current.CancellationToken);

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
        var response = await user.Client.PostAsync("/api/route-imports/image", content, TestContext.Current.CancellationToken);

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
        var response = await user.Client.PostAsync("/api/route-imports/image", content, TestContext.Current.CancellationToken);

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
            await mine.Client.PostAsync("/api/route-imports/image", content, TestContext.Current.CancellationToken);
        }
        using (var content = Upload(area.Id, fileName: "theirs.jpg"))
        {
            await theirs.Client.PostAsync("/api/route-imports/image", content, TestContext.Current.CancellationToken);
        }

        var rows = await mine.Client.GetFromJsonAsync<List<RouteImportRow>>(
            "/api/route-imports/mine", TestContext.Current.CancellationToken);

        rows!.Count.ShouldBe(1);
        rows[0].FileName.ShouldBe("mine.jpg");
        rows[0].Status.ShouldBe("pending");
        rows[0].AreaNamePath.ShouldBe(area.Name);
    }

    // Status reaches the frontend lowercased, like every other enum it narrows to a
    // literal union (see api/types.ts's RouteImportStatus).
    [Fact]
    public async Task Mine_reports_a_decided_rows_route_and_outcome()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var route = await SeedData.CreateRouteAsync(db, area, user.UserId, "La Loca");
            db.RouteImports.Add(new RouteImport
            {
                Kind = RouteImportKind.Image,
                BlobName = "pena/la-loca-01.jpg",
                ContentType = "image/jpeg",
                FileName = "topo.jpg",
                AreaId = area.Id,
                SubmittedByUserId = user.UserId,
                Status = RouteImportStatus.Completed,
                Outcome = "Vía creada y pendiente de validación",
                RouteId = route.Id,
                RouteWasCreated = true,
                ProcessedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var rows = await user.Client.GetFromJsonAsync<List<RouteImportRow>>(
            "/api/route-imports/mine", TestContext.Current.CancellationToken);

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

    // --- The Url kind (plan 31 §1) -------------------------------------------------

    [Fact]
    public async Task Posting_a_url_as_a_plain_user_returns_202_and_enqueues_the_row_id()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/route-imports/url",
            new { url = "https://blog.example.com/apertura-la-loca", areaId = area.Id },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var import = await db.RouteImports.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        import.Kind.ShouldBe(RouteImportKind.Url);
        import.Status.ShouldBe(RouteImportStatus.Pending);
        import.SourceUrl.ShouldBe("https://blog.example.com/apertura-la-loca");
        import.SubmittedByUserId.ShouldBe(user.UserId);
        // Nothing is staged for a Url import — the artifact is the URL on the row.
        import.BlobName.ShouldBeNull();

        var messages = await RouteImportQueueClient.ReceiveMessagesAsync(
            1, cancellationToken: TestContext.Current.CancellationToken);
        JsonSerializer.Deserialize<RouteImportMessage>(messages.Value[0].MessageText)!.ImportId.ShouldBe(import.Id);
    }

    // Canonicalised at submission so a re-paste carrying a tracking parameter is the same
    // submission, which is what the worker's duplicate short-circuit compares on.
    [Fact]
    public async Task A_submitted_url_is_canonicalised_on_the_row()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();

        await user.Client.PostAsJsonAsync(
            "/api/route-imports/url",
            new { url = "HTTPS://Blog.Example.com/posts/la-loca/?utm_source=rss", areaId = area.Id },
            TestContext.Current.CancellationToken);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var import = await db.RouteImports.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        import.SourceUrl.ShouldBe("https://blog.example.com/posts/la-loca");
    }

    // Syntactic validation only, and deliberately so — the network-level guard is the
    // worker's (PageFetcher), because any check made here is advisory by the time it
    // connects. What this buys is telling the user their typo is a typo now rather than
    // after a two-minute wait for a Discarded row saying the same thing.
    [Theory]
    [InlineData("javascript:alert(1)", "algún enlace no es válido: debe empezar por http:// o https://")]
    [InlineData("file:///etc/passwd", "algún enlace no es válido: debe empezar por http:// o https://")]
    [InlineData("no soy una url", "algún enlace no es válido: debe empezar por http:// o https://")]
    [InlineData("", "escribe el enlace de la página")]
    [InlineData("   ", "escribe el enlace de la página")]
    public async Task An_unusable_url_400s_in_spanish_and_enqueues_nothing(string url, string expected)
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/route-imports/url", new { url, areaId = area.Id }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe(expected);

        await AssertNothingHappenedAsync();
    }

    [Fact]
    public async Task An_over_long_url_400s_and_enqueues_nothing()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();

        var url = "https://example.com/" + new string('a', LinkRules.MaxLinkLength);
        var response = await user.Client.PostAsJsonAsync(
            "/api/route-imports/url", new { url, areaId = area.Id }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe("algún enlace es demasiado largo");

        await AssertNothingHappenedAsync();
    }

    [Fact]
    public async Task A_url_import_for_an_unknown_area_400s_and_enqueues_nothing()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);

        var response = await user.Client.PostAsJsonAsync(
            "/api/route-imports/url",
            new { url = "https://blog.example.com/la-loca", areaId = 999999 },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe("el sector no existe");

        await AssertNothingHappenedAsync();
    }

    // One list, both kinds — the page shows whichever of FileName/SourceUrl the row has.
    [Fact]
    public async Task Mine_returns_both_kinds()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();

        using (var content = Upload(area.Id, fileName: "topo.jpg"))
        {
            await user.Client.PostAsync("/api/route-imports/image", content, TestContext.Current.CancellationToken);
        }
        await user.Client.PostAsJsonAsync(
            "/api/route-imports/url",
            new { url = "https://blog.example.com/la-loca", areaId = area.Id },
            TestContext.Current.CancellationToken);

        var rows = await user.Client.GetFromJsonAsync<List<RouteImportRow>>(
            "/api/route-imports/mine", TestContext.Current.CancellationToken);

        rows!.Count.ShouldBe(2);
        rows.Select(r => r.Kind).ShouldBe(["url", "image"], ignoreOrder: true);
        rows.Single(r => r.Kind == "url").SourceUrl.ShouldBe("https://blog.example.com/la-loca");
        rows.Single(r => r.Kind == "url").FileName.ShouldBeNull();
        rows.Single(r => r.Kind == "image").FileName.ShouldBe("topo.jpg");
        rows.Single(r => r.Kind == "image").SourceUrl.ShouldBeNull();
    }

    // --- Delete (plan 33) -----------------------------------------------------------

    private async Task<RouteImport> SeedImportAsync(string userId, Area area, RouteImportStatus status, string? blobName = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var import = new RouteImport
        {
            Kind = blobName is null ? RouteImportKind.Url : RouteImportKind.Image,
            SourceUrl = blobName is null ? "https://blog.example.com/la-loca" : null,
            BlobName = blobName,
            ContentType = blobName is null ? null : "image/jpeg",
            FileName = blobName is null ? null : "topo.jpg",
            AreaId = area.Id,
            SubmittedByUserId = userId,
            Status = status,
            Outcome = "No parece un topo de escalada"
        };
        db.RouteImports.Add(import);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return import;
    }

    [Fact]
    public async Task Deleting_a_discarded_row_returns_204_and_removes_it()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();
        var import = await SeedImportAsync(user.UserId, area, RouteImportStatus.Discarded);

        var response = await user.Client.DeleteAsync($"/api/route-imports/{import.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.RouteImports.AnyAsync(i => i.Id == import.Id, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task Deleting_a_failed_image_row_deletes_its_staged_blob()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();

        var blobStorage = Factory.Services.GetRequiredService<BlobStorageService>();
        var blobName = $"{RouteImportKeys.StagingPrefix}{Guid.NewGuid():N}.jpg";
        await blobStorage.UploadAsync(blobName, new MemoryStream(ImageBytes), "image/jpeg");

        var import = await SeedImportAsync(user.UserId, area, RouteImportStatus.Failed, blobName);

        var response = await user.Client.DeleteAsync($"/api/route-imports/{import.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await blobStorage.DownloadAsync(blobName)).ShouldBeNull();
    }

    [Theory]
    [InlineData(RouteImportStatus.Pending)]
    [InlineData(RouteImportStatus.Completed)]
    public async Task Deleting_a_pending_or_completed_row_409s_and_keeps_it(RouteImportStatus status)
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();
        var import = await SeedImportAsync(user.UserId, area, status);

        var response = await user.Client.DeleteAsync($"/api/route-imports/{import.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe("No se puede eliminar una importación pendiente o completada");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.RouteImports.AnyAsync(i => i.Id == import.Id, TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task Deleting_another_users_row_404s_and_keeps_it()
    {
        using var owner = await TestUsers.CreateAsync(Factory, Roles.User);
        using var other = await TestUsers.CreateAsync(Factory, Roles.User);
        var area = await SeedAreaAsync();
        var import = await SeedImportAsync(owner.UserId, area, RouteImportStatus.Discarded);

        var response = await other.Client.DeleteAsync($"/api/route-imports/{import.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.RouteImports.AnyAsync(i => i.Id == import.Id, TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task Deleting_a_nonexistent_row_404s()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);

        var response = await user.Client.DeleteAsync("/api/route-imports/999999", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task AssertNothingHappenedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.RouteImports.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        (await QueueLengthAsync()).ShouldBe(0);
    }

    private sealed record ErrorResponse(string Error);

    private sealed record RouteImportRow(
        int Id, string Kind, string? FileName, string? SourceUrl, int AreaId, string AreaNamePath,
        DateTimeOffset CreatedAt, string Status, string? Outcome, int? RouteId, string? RouteName,
        string? RouteSlug, string? RouteAreaSlugPath, bool RouteWasCreated, DateTimeOffset? ProcessedAt);
}
