using Acordanza.Core.Services;
using Acordanza.Worker.Services;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// Plan 22 (worker-project-split). Before the split, the worker shared Program.cs's
// prelude, which read Jwt:Key unconditionally and threw before ever reaching the
// process-enrichment branch — a job given only its own Terraform env (no Jwt:Key)
// could never start, and nothing in the build or the test suite could have caught it.
// This builds the worker's actual DI graph (AddEnrichmentWorker) from exactly the keys
// infra/terraform/container_app_job.tf hands the job, no more, and asserts it resolves.
// No Docker — resolving a service graph doesn't open a connection — so this belongs in
// the fast/unit tier and would fail again the moment someone reintroduces a config key
// the job isn't given.
public class WorkerConfigurationTests
{
    [Fact]
    public void The_worker_resolves_from_only_its_terraform_env()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Port=5432;Database=acordanza;Username=acordanza;Password=acordanza",
            ["Storage:AccountName"] = "acordanzaworkertest",
            ["Storage:ContainerName"] = "topos",
            ["Storage:EnrichmentQueueName"] = "route-enrichment",
            ["Storage:RouteImportQueueName"] = "route-import",
            ["Storage:ManagedIdentityClientId"] = Guid.NewGuid().ToString(),
            ["Anthropic:FoundryResource"] = "acordanza-foundry-test",
            ["Anthropic:ManagedIdentityClientId"] = Guid.NewGuid().ToString(),
            ["Amplitude:ApiKey"] = "test-amplitude-key"
        });

        builder.Services.AddEnrichmentWorker(builder.Configuration);
        using var host = builder.Build();
        using var scope = host.Services.CreateScope();

        Should.NotThrow(() => scope.ServiceProvider.GetRequiredService<EnrichmentQueueProcessor>());
        Should.NotThrow(() => scope.ServiceProvider.GetRequiredService<RouteImportQueueProcessor>());
        // The Claude providers, not the no-op pair — Anthropic:FoundryResource is set above,
        // so this asserts the real Foundry-backed graph resolves, not just some graph.
        scope.ServiceProvider.GetRequiredService<IRouteEnrichmentProvider>()
            .ShouldBeOfType<ClaudeRouteEnrichmentProvider>();
        scope.ServiceProvider.GetRequiredService<ITopoVerificationProvider>()
            .ShouldBeOfType<ClaudeTopoVerificationProvider>();
        scope.ServiceProvider.GetRequiredService<ITopoImportGateProvider>()
            .ShouldBeOfType<ClaudeTopoImportGateProvider>();
        scope.ServiceProvider.GetRequiredService<ITopoExtractionProvider>()
            .ShouldBeOfType<ClaudeTopoExtractionProvider>();
        // The real Amplitude tracker, not the no-op one — Amplitude:ApiKey is set above,
        // so this asserts the AddHttpClient<IAnalyticsTracker, AmplitudeAnalyticsTracker>()
        // branch actually resolves, not just the no-op branch exercised elsewhere.
        scope.ServiceProvider.GetRequiredService<IAnalyticsTracker>()
            .ShouldBeOfType<AmplitudeAnalyticsTracker>();
    }

    // The two queue clients must be distinguishable, and the keyed registration is what
    // makes them so: a second unkeyed AddSingleton<QueueClient> would silently win over
    // the enrichment one depending on registration order, and nothing else in the suite
    // would notice until an enrichment message landed on the wrong queue.
    [Fact]
    public void The_two_queues_resolve_to_different_clients()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Port=5432;Database=acordanza;Username=acordanza;Password=acordanza",
            ["Storage:AccountName"] = "acordanzaworkertest",
            ["Storage:ContainerName"] = "topos",
            ["Storage:EnrichmentQueueName"] = "route-enrichment",
            ["Storage:RouteImportQueueName"] = "route-import",
            ["Storage:ManagedIdentityClientId"] = Guid.NewGuid().ToString()
        });

        builder.Services.AddEnrichmentWorker(builder.Configuration);
        using var host = builder.Build();

        host.Services.GetRequiredService<QueueClient>().Name.ShouldBe("route-enrichment");
        host.Services.GetRequiredKeyedService<QueueClient>(RouteImportKeys.QueueServiceKey).Name
            .ShouldBe("route-import");
    }

    // Regression guard: the deployed worker once passed an empty scope array to
    // AnthropicFoundryIdentityTokenCredentials, whose scopes parameter only falls back to
    // Foundry's audience when it's *null* — [] is stored verbatim, so every enrichment failed
    // with "Value cannot be null. (Parameter 'resource')" from MSAL. Nothing else can catch
    // this: the provider constructors build the credential without making a call, so the DI
    // graph resolves perfectly and the failure waits for the first real message.
    [Fact]
    public void The_foundry_client_requests_a_token_on_foundrys_audience()
    {
        FoundryClient.Scopes.ShouldBe(["https://ai.azure.com/.default"]);
    }
}
