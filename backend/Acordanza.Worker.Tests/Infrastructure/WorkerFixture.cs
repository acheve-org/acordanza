using Acordanza.Core.Data;
using Acordanza.Core.Services;
using Acordanza.Worker.Services;
using Azure.Storage.Queues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Testcontainers.Azurite;
using Testcontainers.PostgreSql;
using Xunit;

[assembly: AssemblyFixture(typeof(Acordanza.Worker.Tests.Infrastructure.WorkerFixture))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Acordanza.Worker.Tests.Infrastructure;

// The worker-side equivalent of Acordanza.Api.Tests' ApiFixture — but there is no
// WebApplicationFactory/Program host to boot here: AddEnrichmentWorker registers
// everything a worker process needs (AppDbContext, BlobStorageService, QueueClient,
// RouteEnrichmentService, EnrichmentQueueProcessor) directly on a plain
// IServiceCollection, so this fixture builds that graph itself, against exactly the
// Terraform-shaped env the real worker gets (see backend/CLAUDE.md's Route enrichment
// section) — no Jwt/Cors/Fido2/Frontend keys, since the worker never reads them.
//
// Assembly-scoped for the same reason as ApiFixture: container startup is the
// expensive part, and two differently-configured graphs must never coexist in one
// run. Container startup is deferred to EnsureStartedAsync() (called from
// WorkerTestBase), so a unit-only filtered run never touches Docker.
public sealed class WorkerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:18").Build();

    private readonly AzuriteContainer _azurite =
        new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite")
            .WithCommand("--skipApiVersionCheck") // see docker-compose.yml for why
            .Build();

    private readonly SemaphoreSlim _startLock = new(1, 1);
    private bool _started;

    private string _connectionString = null!;
    private Respawner _respawner = null!;
    private ServiceProvider? _services;

    public ServiceProvider Services =>
        _services ?? throw new InvalidOperationException(
            $"{nameof(WorkerFixture)} hasn't started yet — call {nameof(EnsureStartedAsync)}() first (WorkerTestBase does this automatically).");

    // The topo-import queue is registered under a DI key so it can't shadow the unkeyed
    // enrichment client (see TopoImportKeys.QueueServiceKey) — surfaced here so tests
    // don't each have to remember the key.
    public QueueClient TopoImportQueueClient =>
        Services.GetRequiredKeyedService<QueueClient>(TopoImportKeys.QueueServiceKey);

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async Task EnsureStartedAsync()
    {
        if (_started) return;
        await _startLock.WaitAsync();
        try
        {
            if (_started) return;

            await Task.WhenAll(_postgres.StartAsync(), _azurite.StartAsync());

            _connectionString = _postgres.GetConnectionString();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = _connectionString,
                    ["Storage:ConnectionString"] = _azurite.GetConnectionString(),
                    ["Storage:ContainerName"] = "topos",
                    ["Storage:EnrichmentQueueName"] = "route-enrichment",
                    ["Storage:TopoImportQueueName"] = "topo-import"
                    // No Anthropic:FoundryResource — AddEnrichmentWorker falls back to the
                    // no-op providers; tests that exercise RouteEnrichmentService/
                    // RouteImportService construct them directly with fakes.
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddEnrichmentWorker(configuration);
            _services = services.BuildServiceProvider();

            using (var scope = _services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();

                var blobStorage = scope.ServiceProvider.GetRequiredService<Core.Services.BlobStorageService>();
                await blobStorage.EnsureContainerExistsAsync();
            }

            // Unlike the API host (Program.cs), nothing in the worker's own startup
            // creates the queues — production relies on Terraform provisioning them.
            await _services.GetRequiredService<QueueClient>().CreateIfNotExistsAsync();
            await TopoImportQueueClient.CreateIfNotExistsAsync();

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["identity", "auth", "core"]
            });

            _started = true;
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task ResetDatabaseAsync()
    {
        await EnsureStartedAsync();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);

        await Services.GetRequiredService<QueueClient>().ClearMessagesAsync();
        await TopoImportQueueClient.ClearMessagesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_started) return;

        await _services!.DisposeAsync();
        await _postgres.DisposeAsync();
        await _azurite.DisposeAsync();
    }
}
