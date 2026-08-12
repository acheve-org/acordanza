using Acordanza.Worker.Services;
using Acordanza.Worker.Tests.Infrastructure;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// Moved out of Acordanza.Api.Tests.Controllers.RouteEnrichmentTests (plan 27,
// worker-test-split) — see RouteEnrichmentServiceTests for the rationale.
public class EnrichmentQueueProcessorTests(WorkerFixture fixture) : WorkerTestBase(fixture)
{
    private QueueClient QueueClient => Fixture.Services.GetRequiredService<QueueClient>();

    private async Task<int> QueueLengthAsync()
    {
        var props = await QueueClient.GetPropertiesAsync(TestContext.Current.CancellationToken);
        return props.Value.ApproximateMessagesCount;
    }

    [Fact]
    public async Task An_unparseable_message_is_dropped_without_retrying()
    {
        await QueueClient.SendMessageAsync("not json", TestContext.Current.CancellationToken);

        using var scope = Fixture.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<EnrichmentQueueProcessor>();
        await processor.RunAsync(TestContext.Current.CancellationToken);

        (await QueueLengthAsync()).ShouldBe(0);
    }
}
