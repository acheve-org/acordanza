using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Acordanza.Api.Tests.Infrastructure;

// Base for every Category=Integration test class: resets the shared database to the
// Respawn checkpoint before each test runs, so tests can't see each other's writes
// despite sharing one assembly-wide fixture/container pair.
[Trait("Category", "Integration")]
public abstract class IntegrationTestBase(ApiFixture fixture) : IAsyncLifetime
{
    protected WebApplicationFactory<Program> Factory => fixture.Factory;

    public async ValueTask InitializeAsync() => await fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
