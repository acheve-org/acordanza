using Xunit;

namespace Acordanza.Worker.Tests.Infrastructure;

// Base for every Category=Integration test class here — resets the shared database
// and clears the queue before each test, same pattern as Acordanza.Api.Tests'
// IntegrationTestBase.
[Trait("Category", "Integration")]
public abstract class WorkerTestBase(WorkerFixture fixture) : IAsyncLifetime
{
    protected WorkerFixture Fixture => fixture;

    public async ValueTask InitializeAsync() => await fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
