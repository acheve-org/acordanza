using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Infrastructure;

// Proves Respawn actually resets the database between tests: both tests create a root
// area with the identical name, which Area's unique (ParentAreaId, Name) index (see
// AppDbContext) would reject on the second insert if the first test's row survived.
// Order between the two doesn't matter — whichever runs second is the one that would
// fail if isolation were broken.
public class DatabaseIsolationTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string SharedRootAreaName = "Isolation Check Area";

    [Fact]
    public Task First_test_creates_the_colliding_root_area() => CreatesRootAreaWithSharedNameAsync();

    [Fact]
    public Task Second_test_creates_the_same_named_root_area() => CreatesRootAreaWithSharedNameAsync();

    private async Task CreatesRootAreaWithSharedNameAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Areas.Add(new Area { Name = SharedRootAreaName, Slug = "isolation-check-area" });
        await db.SaveChangesAsync();

        (await db.Areas.CountAsync(a => a.Name == SharedRootAreaName)).ShouldBe(1);
    }
}
