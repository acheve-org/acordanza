using System.Text.Json;
using Acordanza.Core.Services;
using Azure.Storage.Queues;

namespace Acordanza.Api.Services;

// Owns the enqueue side of route import (POST /route-imports, both kinds); the drain side
// lives in Acordanza.Worker's RouteImportQueueProcessor. Sibling of RouteEnrichmentQueue,
// and deliberately a separate type over a separate QueueClient rather than a second method
// on it: the two queues carry different message shapes and different visibility
// timeouts, and the API resolves both clients by their own concrete wrapper.
public class RouteImportQueue(QueueClient queueClient)
{
    public async Task EnqueueAsync(int importId, CancellationToken cancellationToken = default)
    {
        var message = JsonSerializer.Serialize(new RouteImportMessage(importId));
        await queueClient.SendMessageAsync(message, cancellationToken: cancellationToken);
    }
}
