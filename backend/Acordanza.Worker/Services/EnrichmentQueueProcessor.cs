using Acordanza.Core.Services;
using System.Diagnostics;
using System.Text.Json;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

namespace Acordanza.Worker.Services;

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

    // Unlike the API, nothing (no ASP.NET Core request) automatically opens a span
    // around "one unit of work" here — without this, every DB/HTTP call an enrichment
    // makes shows up as its own disconnected root trace instead of children of one
    // "processed this message" trace. Registered via .AddSource in AddEnrichmentWorker.
    private static readonly ActivitySource ActivitySource = new("Acordanza.Worker.EnrichmentQueueProcessor");

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("enrichment drain started, polling queue {QueueName}", queueClient.Name);
        var totalProcessed = 0;
        while (true)
        {
            var response = await queueClient.ReceiveMessagesAsync(BatchSize, VisibilityTimeout, cancellationToken);
            var messages = response.Value;
            logger.LogInformation("enrichment received {Count} message(s)", messages.Length);
            if (messages.Length == 0) break;

            foreach (var message in messages)
            {
                await ProcessAsync(message, cancellationToken);
                totalProcessed++;
            }
        }
        logger.LogInformation("enrichment drain finished, processed {Count} message(s)", totalProcessed);
    }

    private async Task ProcessAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("enrichment.process_message", ActivityKind.Consumer);
        activity?.SetTag("messaging.message.id", message.MessageId);
        activity?.SetTag("messaging.azure.dequeue_count", message.DequeueCount);

        EnrichmentMessage? body;
        try
        {
            body = JsonSerializer.Deserialize<EnrichmentMessage>(message.MessageText);
        }
        catch (JsonException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "unparseable message");
            logger.LogError(ex, "enrichment poison message (unparseable), dropping: {Body}", message.MessageText);
            await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            return;
        }

        if (body is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "null message body");
            logger.LogError("enrichment poison message (null body), dropping: {Body}", message.MessageText);
            await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            return;
        }

        activity?.SetTag("acordanza.route_id", body.RouteId);
        activity?.SetTag("acordanza.force", body.Force);

        logger.LogInformation("enrichment processing route {RouteId} (force={Force}, dequeueCount={Count})",
            body.RouteId, body.Force, message.DequeueCount);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<RouteEnrichmentService>();
            await service.EnrichAsync(body.RouteId, body.Force, cancellationToken);
            await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            logger.LogInformation("enrichment message for route {RouteId} deleted from queue", body.RouteId);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            if (message.DequeueCount > MaxDeliveryAttempts)
            {
                activity?.SetTag("acordanza.enrichment.outcome", "poison");
                logger.LogError(ex, "enrichment poison message for route {RouteId}, dropping after {Count} attempts",
                    body.RouteId, message.DequeueCount);
                await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            }
            else
            {
                activity?.SetTag("acordanza.enrichment.outcome", "retry");
                logger.LogError(ex, "enrichment failed for route {RouteId} (attempt {Count}), will be redelivered",
                    body.RouteId, message.DequeueCount);
                // Left in place — the visibility timeout expiring redelivers it.
            }
        }
    }
}
