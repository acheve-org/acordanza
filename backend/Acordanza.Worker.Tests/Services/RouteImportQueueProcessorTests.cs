using System.Text.Json;
using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Acordanza.Core.Services;
using Acordanza.Worker.Services;
using Acordanza.Worker.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// Plan 30 §3. The sibling of EnrichmentQueueProcessorTests, plus the one behaviour that
// has no enrichment counterpart: a message this processor finally gives up on marks its
// row Failed. Enrichment can drop a poison message and move on because the route still
// exists; here the row *is* the artifact, and a row left Pending forever is precisely the
// "waited two minutes for silence" outcome plan 30 exists to prevent.
public class RouteImportQueueProcessorTests(WorkerFixture fixture) : WorkerTestBase(fixture)
{
    private async Task<int> QueueLengthAsync()
    {
        var props = await Fixture.RouteImportQueueClient.GetPropertiesAsync(TestContext.Current.CancellationToken);
        return props.Value.ApproximateMessagesCount;
    }

    [Fact]
    public async Task An_unparseable_message_is_dropped_without_retrying()
    {
        await Fixture.RouteImportQueueClient.SendMessageAsync("not json", TestContext.Current.CancellationToken);

        using var scope = Fixture.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<RouteImportQueueProcessor>();
        await processor.RunAsync(TestContext.Current.CancellationToken);

        (await QueueLengthAsync()).ShouldBe(0);
    }

    // The two queues must not read each other's messages. Without the keyed registration
    // this is exactly what would break — and silently, since an EnrichmentMessage happens
    // to deserialize into a RouteImportMessage with ImportId 0.
    [Fact]
    public async Task The_topo_import_processor_only_drains_its_own_queue()
    {
        var enrichmentQueue = Fixture.Services.GetRequiredService<Azure.Storage.Queues.QueueClient>();
        await enrichmentQueue.SendMessageAsync(
            JsonSerializer.Serialize(new EnrichmentMessage(1, false)), TestContext.Current.CancellationToken);

        using var scope = Fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RouteImportQueueProcessor>()
            .RunAsync(TestContext.Current.CancellationToken);

        var props = await enrichmentQueue.GetPropertiesAsync(TestContext.Current.CancellationToken);
        props.Value.ApproximateMessagesCount.ShouldBe(1);
    }

    // A message for an import that no longer exists is a no-op, not an exception: the
    // service logs and returns, so the message is deleted rather than redelivered five times.
    [Fact]
    public async Task A_message_for_a_missing_import_is_deleted()
    {
        await Fixture.RouteImportQueueClient.SendMessageAsync(
            JsonSerializer.Serialize(new RouteImportMessage(9999)), TestContext.Current.CancellationToken);

        using var scope = Fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RouteImportQueueProcessor>()
            .RunAsync(TestContext.Current.CancellationToken);

        (await QueueLengthAsync()).ShouldBe(0);
    }

    // End to end through the real DI graph: no Anthropic:FoundryResource is configured in
    // the fixture, so the no-op gate answers Completed: false and the row lands Failed —
    // which is the honest reading of "this environment never looked at the image", and the
    // one state that tells the uploader to try again rather than that their topo was bad.
    [Fact]
    public async Task A_drain_without_foundry_configured_marks_the_row_failed_not_discarded()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await SeedData.CreateAreaAsync(db, "Peña Rúa");

        var blobStorage = scope.ServiceProvider.GetRequiredService<BlobStorageService>();
        var blobName = $"{RouteImportKeys.StagingPrefix}{Guid.NewGuid():N}.jpg";
        await blobStorage.UploadAsync(blobName, new MemoryStream([0xFF, 0xD8, 0xFF, 0xD9]), "image/jpeg");

        var import = new RouteImport
        {
            BlobName = blobName,
            ContentType = "image/jpeg",
            FileName = "topo.jpg",
            AreaId = area.Id,
            SubmittedByUserId = user.Id,
            Status = RouteImportStatus.Pending
        };
        db.RouteImports.Add(import);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Fixture.RouteImportQueueClient.SendMessageAsync(
            JsonSerializer.Serialize(new RouteImportMessage(import.Id)), TestContext.Current.CancellationToken);

        await scope.ServiceProvider.GetRequiredService<RouteImportQueueProcessor>()
            .RunAsync(TestContext.Current.CancellationToken);

        (await QueueLengthAsync()).ShouldBe(0);
        var reloaded = await db.RouteImports.AsNoTracking()
            .FirstAsync(i => i.Id == import.Id, TestContext.Current.CancellationToken);
        reloaded.Status.ShouldBe(RouteImportStatus.Failed);
        reloaded.Outcome.ShouldBe(RouteImportOutcomes.Failed);
        reloaded.ProcessedAt.ShouldNotBeNull();
    }
}
