using Acordanza.Core.Data;
using Acordanza.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Drain-and-exit: what the Container Apps Job runs (event-driven, scales from
// zero via KEDA) — see infra/terraform/container_app_job.tf. No args, no CLI
// branch: `process-enrichment` was a subcommand only because it shared a
// binary with the API; now the binary is the worker.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddEnrichmentWorker(builder.Configuration);

using var app = builder.Build();

// StartAsync/StopAsync (not just Build) because OpenTelemetry.Extensions.Hosting's
// TracerProvider/MeterProvider are only instantiated and flushed by the hosted
// service it registers — a drain-and-exit job that never starts the host never
// exports a trace or metric, and never flushes the OTLP batch exporter before exit.
await app.StartAsync();

var providerConfig = app.Services.GetRequiredService<EnrichmentProviderConfig>();
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
startupLogger.LogInformation(
    "enrichment worker starting; Anthropic:FoundryResource configured={Configured}, provider={Provider}",
    providerConfig.FoundryResourceConfigured,
    providerConfig.FoundryResourceConfigured ? nameof(ClaudeRouteEnrichmentProvider) : nameof(NoOpRouteEnrichmentProvider));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    // Both queues, in sequence, then exit. One binary, no branches, two drains (plan 30
    // §3): a second job would mean a second Terraform resource and a second deploy step,
    // and a CLI arg selecting which queue to drain would re-introduce exactly the
    // shared-configuration-between-two-processes failure plan 22 exists to prevent.
    // KEDA has a scale rule per queue, so an execution may well be fired by one of them
    // and find the other empty — draining an empty queue is one cheap receive call.
    var enrichmentProcessor = scope.ServiceProvider.GetRequiredService<EnrichmentQueueProcessor>();
    await enrichmentProcessor.RunAsync();

    var routeImportProcessor = scope.ServiceProvider.GetRequiredService<RouteImportQueueProcessor>();
    await routeImportProcessor.RunAsync();
}

await app.StopAsync();
