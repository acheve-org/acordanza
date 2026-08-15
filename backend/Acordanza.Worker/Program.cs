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

RouteImportDrainResult importDrain;

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
    importDrain = await routeImportProcessor.RunAsync();
}

await app.StopAsync();

// A dropped route import fails the execution. Container Apps reads the container's exit
// code and `replica_retry_limit = 0` (infra/terraform/container_app_job.tf) means a failed
// execution stays failed rather than being re-run into a green one — so this is the signal
// that makes a broken import pipeline visible without reading the logs. Deliberately after
// StopAsync, not thrown from the drain: the telemetry that explains the failure has to be
// flushed before the process leaves.
if (importDrain.Poisoned > 0)
{
    startupLogger.LogError(
        "route import dropped {Poisoned} of {Processed} message(s) as poison; failing the job execution",
        importDrain.Poisoned, importDrain.Processed);
    return 1;
}

return 0;
