using Acordanza.Core.Data;
using Acordanza.Core.Services;
using Azure.Identity;
using Azure.Storage.Queues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Acordanza.Worker.Services;

// One definition of "what the enrichment worker is" — the DI graph an actual
// process run (Program.cs) and the integration tests (RouteEnrichmentTests,
// WorkerConfigurationTests) both build off, so they can never quietly diverge.
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEnrichmentWorker(this IServiceCollection services, IConfiguration configuration)
    {
        // Traces + metrics + logs all export via OTLP to whatever
        // OTEL_EXPORTER_OTLP_ENDPOINT/HEADERS point at (Grafana Cloud in
        // production — see infra/terraform/container_app_job.tf); unset locally,
        // so the exporters just have nowhere to send and dev is unaffected.
        // The service name is set here, in code, rather than left to
        // OTEL_RESOURCE_ATTRIBUTES's service.name — an explicit AddService call
        // always wins over the env var, so this is the one place it's written.
        var otelResource = ResourceBuilder.CreateDefault().AddService("acordanza-enrichment-worker");

        services.AddLogging(logging => logging.AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(otelResource);
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.AddOtlpExporter();
        }));

        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .SetResourceBuilder(otelResource)
                .AddSource("Acordanza.Worker.EnrichmentQueueProcessor")
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .SetResourceBuilder(otelResource)
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddNpgsqlInstrumentation()
                .AddOtlpExporter());

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Default configuration");
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        services.AddSingleton<BlobStorageService>();
        services.AddSingleton<MediaUploadService>();
        services.AddScoped<AreaHierarchyService>();
        services.AddHttpClient();

        // Route enrichment queue (Storage Queue on the same account topo images use).
        // Same dual-credential pattern as BlobStorageService: Azurite locally via a
        // connection string, the container app's managed identity in production.
        var storageSection = configuration.GetSection("Storage");
        var enrichmentQueueName = storageSection["EnrichmentQueueName"]
            ?? throw new InvalidOperationException("Missing Storage:EnrichmentQueueName configuration");
        var storageConnectionString = storageSection["ConnectionString"];
        var queueClientOptions = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };

        QueueClient enrichmentQueueClient;
        if (!string.IsNullOrEmpty(storageConnectionString))
        {
            enrichmentQueueClient = new QueueClient(storageConnectionString, enrichmentQueueName, queueClientOptions);
        }
        else
        {
            var accountName = storageSection["AccountName"]
                ?? throw new InvalidOperationException("Missing Storage:AccountName configuration");
            var managedIdentityClientId = storageSection["ManagedIdentityClientId"]
                ?? throw new InvalidOperationException("Missing Storage:ManagedIdentityClientId configuration");

            var credential = new DefaultAzureCredential(
                new DefaultAzureCredentialOptions { ManagedIdentityClientId = managedIdentityClientId });
            enrichmentQueueClient = new QueueClient(
                new Uri($"https://{accountName}.queue.core.windows.net/{enrichmentQueueName}"), credential, queueClientOptions);
        }
        services.AddSingleton(enrichmentQueueClient);

        services.AddScoped<RouteEnrichmentService>();
        services.AddScoped<EnrichmentQueueProcessor>();

        // Without Anthropic:FoundryResource configured, enrichment is skipped rather
        // than failing (see IRouteEnrichmentProvider.cs) — the queue/worker stays
        // exercisable locally without an Azure AI Foundry resource or az login session.
        if (string.IsNullOrEmpty(configuration["Anthropic:FoundryResource"]))
        {
            services.AddScoped<IRouteEnrichmentProvider, NoOpRouteEnrichmentProvider>();
            services.AddScoped<ITopoVerificationProvider, NoOpTopoVerificationProvider>();
        }
        else
        {
            services.AddScoped<IRouteEnrichmentProvider, ClaudeRouteEnrichmentProvider>();
            services.AddScoped<ITopoVerificationProvider, ClaudeTopoVerificationProvider>();
        }

        return services;
    }
}
