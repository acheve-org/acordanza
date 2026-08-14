using System.Diagnostics;
using System.Text.Json;
using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Acordanza.Core.Services;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Acordanza.Worker.Services;

// Drains the route-import queue and exits — mirrors EnrichmentQueueProcessor, on its own
// queue. Two queues rather than one with a discriminated union: EnrichmentQueueProcessor
// treats an undeserializable message as poison, which a second shape on the same queue
// would trip, and each queue wants its own visibility timeout — enrichment needs five
// minutes for a web-search loop, a route import two.
//
// Both import kinds ride *this* queue, though (plan 31 §1): the message is a row id, and
// which reader runs is a column on the row.
//
// The one behavioural difference from its sibling: when a message is finally given up on,
// this marks the row Failed. Enrichment can drop a poison message and move on because the
// route still exists; here the row *is* the artifact, and leaving it Pending forever is
// the broken-feature outcome plans 30 and 31 exist to prevent.
public class RouteImportQueueProcessor(
    [FromKeyedServices(RouteImportKeys.QueueServiceKey)] QueueClient queueClient,
    IServiceScopeFactory scopeFactory,
    ILogger<RouteImportQueueProcessor> logger)
{
    private const int BatchSize = 8;
    // Two minutes: a fetch plus one extraction plus up to eight vision calls is the Url
    // kind's worst case, against the Image kind's pair of vision calls and enrichment's
    // five-minute search loop.
    private static readonly TimeSpan VisibilityTimeout = TimeSpan.FromMinutes(2);
    private const int MaxDeliveryAttempts = 5;

    private static readonly ActivitySource ActivitySource = new("Acordanza.Worker.RouteImportQueueProcessor");

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("route import drain started, polling queue {QueueName}", queueClient.Name);
        var totalProcessed = 0;
        while (true)
        {
            var response = await queueClient.ReceiveMessagesAsync(BatchSize, VisibilityTimeout, cancellationToken);
            var messages = response.Value;
            logger.LogInformation("route import received {Count} message(s)", messages.Length);
            if (messages.Length == 0) break;

            foreach (var message in messages)
            {
                await ProcessAsync(message, cancellationToken);
                totalProcessed++;
            }
        }
        logger.LogInformation("route import drain finished, processed {Count} message(s)", totalProcessed);
    }

    private async Task ProcessAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("route_import.process_message", ActivityKind.Consumer);
        activity?.SetTag("messaging.message.id", message.MessageId);
        activity?.SetTag("messaging.azure.dequeue_count", message.DequeueCount);

        RouteImportMessage? body;
        try
        {
            body = JsonSerializer.Deserialize<RouteImportMessage>(message.MessageText);
        }
        catch (JsonException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "unparseable message");
            logger.LogError(ex, "route import poison message (unparseable), dropping: {Body}", message.MessageText);
            await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            return;
        }

        if (body is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "null message body");
            logger.LogError("route import poison message (null body), dropping: {Body}", message.MessageText);
            await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            return;
        }

        activity?.SetTag("acordanza.route_import_id", body.ImportId);
        logger.LogInformation("route import processing import {ImportId} (dequeueCount={Count})",
            body.ImportId, message.DequeueCount);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<RouteImportService>();
            await service.ImportAsync(body.ImportId, cancellationToken);
            await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            logger.LogInformation("route import message for import {ImportId} deleted from queue", body.ImportId);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            if (message.DequeueCount > MaxDeliveryAttempts)
            {
                activity?.SetTag("acordanza.route_import.outcome", "poison");
                logger.LogError(ex, "route import poison message for import {ImportId}, dropping after {Count} attempts",
                    body.ImportId, message.DequeueCount);
                await MarkFailedAsync(body.ImportId, cancellationToken);
                await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            }
            else
            {
                activity?.SetTag("acordanza.route_import.outcome", "retry");
                logger.LogError(ex, "route import failed for import {ImportId} (attempt {Count}), will be redelivered",
                    body.ImportId, message.DequeueCount);
                // Left in place — the visibility timeout expiring redelivers it.
            }
        }
    }

    // Best-effort, and deliberately so: this runs while already handling a failure, and a
    // second one here must not stop the poison message from being dropped — a message that
    // survives its own giving-up is redelivered forever.
    private async Task MarkFailedAsync(int importId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.RouteImports
                .Where(i => i.Id == importId && i.Status == RouteImportStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, RouteImportStatus.Failed)
                    .SetProperty(i => i.Outcome, RouteImportOutcomes.Failed)
                    .SetProperty(i => i.ProcessedAt, DateTimeOffset.UtcNow), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "route import could not mark import {ImportId} as failed", importId);
        }
    }
}
