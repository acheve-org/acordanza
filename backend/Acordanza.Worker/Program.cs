using Acordanza.Core.Data;
using Acordanza.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Drain-and-exit: what the Container Apps Job runs (event-driven, scales from
// zero via KEDA) — see infra/terraform/container_app_job.tf. No args, no CLI
// branch: `process-enrichment` was a subcommand only because it shared a
// binary with the API; now the binary is the worker.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddEnrichmentWorker(builder.Configuration);

using var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var processor = scope.ServiceProvider.GetRequiredService<EnrichmentQueueProcessor>();
    await processor.RunAsync();
}
