using System.Text.Json;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

namespace Acordanza.Api.Services;

// Drains the route-enrichment queue and exits — the shape a Container Apps Job needs:
// KEDA fires an execution when the queue has messages, this loop processes everything
// currently queued, then returns and the environment scales back to zero. Lives outside
// Program.cs so integration tests can drive it directly against the fixture's
// Postgres + Azurite (see RouteEnrichmentTests).
public class EnrichmentQueueProcessor(
    QueueClient queueClient,
    IServiceScopeFactory scopeFactory,
    ILogger<EnrichmentQueueProcessor> logger)
{
    private const int BatchSize = 8;
    private static readonly TimeSpan VisibilityTimeout = TimeSpan.FromMinutes(5);
    private const int MaxDeliveryAttempts = 5;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var response = await queueClient.ReceiveMessagesAsync(BatchSize, VisibilityTimeout, cancellationToken);
            var messages = response.Value;
            if (messages.Length == 0) break;

            foreach (var message in messages)
            {
                await ProcessAsync(message, cancellationToken);
            }
        }
    }

    private async Task ProcessAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        EnrichmentMessage? body;
        try
        {
            body = JsonSerializer.Deserialize<EnrichmentMessage>(message.MessageText);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "enrichment poison message (unparseable), dropping: {Body}", message.MessageText);
            await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            return;
        }

        if (body is null)
        {
            logger.LogError("enrichment poison message (null body), dropping: {Body}", message.MessageText);
            await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<RouteEnrichmentService>();
            await service.EnrichAsync(body.RouteId, body.Force, cancellationToken);
            await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
        }
        catch (Exception ex)
        {
            if (message.DequeueCount > MaxDeliveryAttempts)
            {
                logger.LogError(ex, "enrichment poison message for route {RouteId}, dropping after {Count} attempts",
                    body.RouteId, message.DequeueCount);
                await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            }
            else
            {
                logger.LogError(ex, "enrichment failed for route {RouteId} (attempt {Count}), will be redelivered",
                    body.RouteId, message.DequeueCount);
                // Left in place — the visibility timeout expiring redelivers it.
            }
        }
    }
}
