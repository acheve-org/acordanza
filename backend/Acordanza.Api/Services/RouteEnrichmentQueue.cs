using System.Text.Json;
using Acordanza.Core.Services;
using Azure.Storage.Queues;

namespace Acordanza.Api.Services;

// Owns the QueueClient and the enqueue side of route enrichment (POST /routes/{id}/enrich,
// the enqueue-enrichment CLI branch). The drain side lives in EnrichmentQueueProcessor.
public class RouteEnrichmentQueue(QueueClient queueClient)
{
    public async Task EnqueueAsync(int routeId, bool force, CancellationToken cancellationToken = default)
    {
        var message = JsonSerializer.Serialize(new EnrichmentMessage(routeId, force));
        await queueClient.SendMessageAsync(message, cancellationToken: cancellationToken);
    }
}
