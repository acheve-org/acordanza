using System.Text.Json;
using Acordanza.Core.Services;
using Azure.Storage.Queues;

namespace Acordanza.Api.Services;

// Owns the enqueue side of topo import (POST /topo-imports); the drain side lives in
// Acordanza.Worker's TopoImportQueueProcessor. Sibling of RouteEnrichmentQueue, and
// deliberately a separate type over a separate QueueClient rather than a second method
// on it: the two queues carry different message shapes and different visibility
// timeouts, and the API resolves both clients by their own concrete wrapper.
public class TopoImportQueue(QueueClient queueClient)
{
    public async Task EnqueueAsync(int importId, CancellationToken cancellationToken = default)
    {
        var message = JsonSerializer.Serialize(new TopoImportMessage(importId));
        await queueClient.SendMessageAsync(message, cancellationToken: cancellationToken);
    }
}
