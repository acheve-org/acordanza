using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Respawn;
using Testcontainers.Azurite;
using Testcontainers.PostgreSql;
using Xunit;

[assembly: AssemblyFixture(typeof(Acordanza.Api.Tests.Infrastructure.ApiFixture))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Acordanza.Api.Tests.Infrastructure;

// Assembly-scoped (not per-class): container startup is the expensive part, and the
// env-var config seam Program.cs reads from is process-global, so two differently
// configured hosts must never exist side by side in the same test run.
//
// xunit.v3 constructs this fixture (and calls InitializeAsync) once for every test run
// of the assembly, regardless of --filter — a unit-only run (`--filter
// "Category!=Integration"`) still instantiates it even though none of the selected
// tests ever touch it. InitializeAsync is therefore a no-op; actual container startup
// is deferred to EnsureStartedAsync(), called only from IntegrationTestBase, so the
// unit-only tier stays genuinely Docker-free.
public sealed class ApiFixture : IAsyncLifetime
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
    private WebApplicationFactory<Program>? _factory;

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException(
            $"{nameof(ApiFixture)} hasn't started yet — call {nameof(EnsureStartedAsync)}() first (IntegrationTestBase does this automatically).");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    // Idempotent, safe to call from every integration test's setup: starts the
    // containers and boots the host on first call only, no-ops after.
    public async Task EnsureStartedAsync()
    {
        if (_started) return;
        await _startLock.WaitAsync();
        try
        {
            if (_started) return;

            await Task.WhenAll(_postgres.StartAsync(), _azurite.StartAsync());

            _connectionString = _postgres.GetConnectionString();

            // Program.cs reads these off builder.Configuration *before* builder.Build(),
            // earlier than WebApplicationFactory's configuration hooks can reach — see
            // plan 08's "What the spike established". Process environment variables are
            // the only seam that lands early enough.
            Environment.SetEnvironmentVariable("ConnectionStrings__Default", _connectionString);
            Environment.SetEnvironmentVariable("Storage__ConnectionString", _azurite.GetConnectionString());
            Environment.SetEnvironmentVariable("Storage__ContainerName", "topos");
            Environment.SetEnvironmentVariable("Storage__DataProtectionContainerName", "dataprotection-keys");
            Environment.SetEnvironmentVariable("Jwt__Key", "integration-test-signing-key-not-used-in-production-0123456789");
            Environment.SetEnvironmentVariable("Jwt__Issuer", "Acordanza.Api");
            Environment.SetEnvironmentVariable("Jwt__Audience", "Acordanza.Client");
            Environment.SetEnvironmentVariable("Jwt__ExpiryMinutes", "60");
            Environment.SetEnvironmentVariable("Frontend__BaseUrl", "http://localhost:5173");
            Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", "http://localhost:5173");
            Environment.SetEnvironmentVariable("Fido2__ServerDomain", "localhost");
            Environment.SetEnvironmentVariable("Fido2__ServerName", "Acordanza");

            var factory = new WebApplicationFactory<Program>();

            // Force the host to actually start (migrations, role seeding, blob container
            // provisioning all happen in Program.cs's top-level startup block) before any
            // test or the Respawn checkpoint below relies on the database being ready.
            using (factory.CreateClient()) { }

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["identity", "auth", "core"],
                // App startup (RoleSeeder) seeds these once; keeping them out of the reset
                // avoids re-seeding roles on every test.
                TablesToIgnore = [new Respawn.Graph.Table("identity", "AspNetRoles")]
            });

            _factory = factory;
            _started = true;
        }
        finally
        {
            _startLock.Release();
        }
    }

    // Called between tests (see IntegrationTestBase) to reset every app table back to
    // empty, keeping the seeded roles.
    public async Task ResetDatabaseAsync()
    {
        await EnsureStartedAsync();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_started) return;

        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
        await _azurite.DisposeAsync();
    }
}
