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

// Cheapest way to confirm at startup which IRouteEnrichmentProvider actually got
// registered, without ILoggerFactory being buildable yet inside AddEnrichmentWorker
// itself — Program.cs logs this once the host is built.
public record EnrichmentProviderConfig(bool FoundryResourceConfigured);

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
                .AddSource("Acordanza.Worker.TopoImportQueueProcessor")
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

        // Topo import (plan 30) rides a second queue on the same account. Registered under
        // a DI key so it can't shadow the unkeyed enrichment client above — which both test
        // fixtures resolve by bare type.
        var topoImportQueueName = storageSection["TopoImportQueueName"]
            ?? throw new InvalidOperationException("Missing Storage:TopoImportQueueName configuration");

        QueueClient topoImportQueueClient;
        if (!string.IsNullOrEmpty(storageConnectionString))
        {
            topoImportQueueClient = new QueueClient(storageConnectionString, topoImportQueueName, queueClientOptions);
        }
        else
        {
            var accountName = storageSection["AccountName"]
                ?? throw new InvalidOperationException("Missing Storage:AccountName configuration");
            var managedIdentityClientId = storageSection["ManagedIdentityClientId"]
                ?? throw new InvalidOperationException("Missing Storage:ManagedIdentityClientId configuration");

            var credential = new DefaultAzureCredential(
                new DefaultAzureCredentialOptions { ManagedIdentityClientId = managedIdentityClientId });
            topoImportQueueClient = new QueueClient(
                new Uri($"https://{accountName}.queue.core.windows.net/{topoImportQueueName}"), credential, queueClientOptions);
        }
        services.AddKeyedSingleton(TopoImportKeys.QueueServiceKey, topoImportQueueClient);

        services.AddScoped<RouteEnrichmentService>();
        services.AddScoped<EnrichmentQueueProcessor>();
        services.AddScoped<RouteImportService>();
        services.AddScoped<TopoImportQueueProcessor>();
        // Singleton: the few-shot example set is downloaded once per process and reused
        // across every message in a drain — see TopoImportFewShotSet.
        services.AddSingleton<TopoImportFewShotSet>();

        // Without Anthropic:FoundryResource configured, enrichment is skipped rather
        // than failing (see IRouteEnrichmentProvider.cs) — the queue/worker stays
        // exercisable locally without an Azure AI Foundry resource or az login session.
        var foundryResourceConfigured = !string.IsNullOrEmpty(configuration["Anthropic:FoundryResource"]);
        if (!foundryResourceConfigured)
        {
            services.AddScoped<IRouteEnrichmentProvider, NoOpRouteEnrichmentProvider>();
            services.AddScoped<ITopoVerificationProvider, NoOpTopoVerificationProvider>();
            services.AddScoped<ITopoImportGateProvider, NoOpTopoImportGateProvider>();
            services.AddScoped<ITopoExtractionProvider, NoOpTopoExtractionProvider>();
        }
        else
        {
            services.AddScoped<IRouteEnrichmentProvider, ClaudeRouteEnrichmentProvider>();
            services.AddScoped<ITopoVerificationProvider, ClaudeTopoVerificationProvider>();
            services.AddScoped<ITopoImportGateProvider, ClaudeTopoImportGateProvider>();
            services.AddScoped<ITopoExtractionProvider, ClaudeTopoExtractionProvider>();
        }

        services.AddSingleton(new EnrichmentProviderConfig(foundryResourceConfigured));

        return services;
    }
}
