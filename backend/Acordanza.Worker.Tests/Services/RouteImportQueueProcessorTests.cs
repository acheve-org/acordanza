using System.Text.Json;
using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Acordanza.Core.Services;
using Acordanza.Worker.Services;
using Acordanza.Worker.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
        var result = await processor.RunAsync(TestContext.Current.CancellationToken);

        (await QueueLengthAsync()).ShouldBe(0);
        // Dropped without ever completing, so the execution has to go red — the plan-32
        // schema regression spent a day looking green precisely because it didn't.
        result.Poisoned.ShouldBe(1);
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
        var result = await scope.ServiceProvider.GetRequiredService<RouteImportQueueProcessor>()
            .RunAsync(TestContext.Current.CancellationToken);

        (await QueueLengthAsync()).ShouldBe(0);
        // The service logged and returned rather than throwing, so this is a *completed*
        // message, not a dropped one — the job stays green.
        result.Poisoned.ShouldBe(0);
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

        var result = await scope.ServiceProvider.GetRequiredService<RouteImportQueueProcessor>()
            .RunAsync(TestContext.Current.CancellationToken);

        (await QueueLengthAsync()).ShouldBe(0);
        var reloaded = await db.RouteImports.AsNoTracking()
            .FirstAsync(i => i.Id == import.Id, TestContext.Current.CancellationToken);
        reloaded.Status.ShouldBe(RouteImportStatus.Failed);
        reloaded.Outcome.ShouldBe(RouteImportOutcomes.Failed);
        reloaded.ProcessedAt.ShouldNotBeNull();
        // The load-bearing half of the new exit code: a Failed *row* is a decided message,
        // not a dropped one. Locally (and on any deploy without Foundry) every import lands
        // here, so counting it as poison would paint every execution red for a worker doing
        // exactly what it is configured to do.
        result.Poisoned.ShouldBe(0);
    }

    // The branch that actually bit in production: ImportAsync throws, the message is
    // redelivered, and on the attempt past MaxDeliveryAttempts it is dropped. Driven with a
    // scope factory that cannot resolve RouteImportService — GetRequiredService throws
    // inside the same try/catch a Foundry 400 lands in, which is the point, and needs no
    // Foundry access to reproduce. AppDbContext is still registered so MarkFailedAsync can
    // do its job.
    private RouteImportQueueProcessor ProcessorThatCannotImport()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(Fixture.Services.GetRequiredService<DbContextOptions<AppDbContext>>()));

        return new RouteImportQueueProcessor(
            Fixture.RouteImportQueueClient,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RouteImportQueueProcessor>.Instance);
    }

    // Receives and immediately re-exposes the message, so the next receive counts as
    // another delivery attempt. Azure increments DequeueCount per receive; there is no
    // other way to reach the poison threshold without waiting out five visibility timeouts.
    private async Task BurnDeliveryAttemptsAsync(int attempts)
    {
        for (var i = 0; i < attempts; i++)
        {
            var received = await Fixture.RouteImportQueueClient.ReceiveMessagesAsync(
                1, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            var message = received.Value.Single();
            await Fixture.RouteImportQueueClient.UpdateMessageAsync(
                message.MessageId, message.PopReceipt, visibilityTimeout: TimeSpan.Zero,
                cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task A_failure_with_attempts_left_is_redelivered_and_does_not_fail_the_job()
    {
        await Fixture.RouteImportQueueClient.SendMessageAsync(
            JsonSerializer.Serialize(new RouteImportMessage(1)), TestContext.Current.CancellationToken);

        var result = await ProcessorThatCannotImport().RunAsync(TestContext.Current.CancellationToken);

        // Left on the queue for the visibility timeout to redeliver, and emphatically not
        // poison: failing the execution on a first transient blip would make the signal
        // worthless.
        result.Poisoned.ShouldBe(0);
        (await QueueLengthAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_failure_past_the_delivery_limit_is_dropped_and_fails_the_job()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await SeedData.CreateAreaAsync(db, "Peña Solano");

        var import = new RouteImport
        {
            BlobName = $"{RouteImportKeys.StagingPrefix}{Guid.NewGuid():N}.jpg",
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
        await BurnDeliveryAttemptsAsync(5);

        var result = await ProcessorThatCannotImport().RunAsync(TestContext.Current.CancellationToken);

        result.Poisoned.ShouldBe(1);
        (await QueueLengthAsync()).ShouldBe(0);
        // Dropping the message and marking the row are unchanged — the exit code is added
        // on top of them, not instead of them.
        var reloaded = await db.RouteImports.AsNoTracking()
            .FirstAsync(i => i.Id == import.Id, TestContext.Current.CancellationToken);
        reloaded.Status.ShouldBe(RouteImportStatus.Failed);
    }
}
