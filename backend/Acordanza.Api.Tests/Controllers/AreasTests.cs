using System.Data.Common;
using System.Net.Http.Json;
using System.Threading;
using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// GET /areas/tree (plan 35 §1) — the whole area tree, flat and geometry-free, that
// AreaPicker/TreeSelect holds client-side. Visibility follows the same
// ValidationVisibility.Areas rule as every other browse list (plan 11); RouteCount is
// each area's own routes only, itself filtered by ValidationVisibility.Routes.
public class AreasTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Returns_every_approved_area_flat_with_parent_ids_intact()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var parent = await SeedData.CreateAreaAsync(db, $"Parent {Guid.NewGuid():N}");
        var child = await SeedData.CreateAreaAsync(db, $"Child {Guid.NewGuid():N}", parent);

        using var caller = await TestUsers.CreateAsync(Factory, Roles.User);
        var tree = await caller.Client.GetFromJsonAsync<List<AreaTreeNodeDto>>(
            "/api/areas/tree", TestContext.Current.CancellationToken);

        tree.ShouldNotBeNull();
        var parentNode = tree.Single(n => n.Id == parent.Id);
        parentNode.ParentAreaId.ShouldBeNull();
        var childNode = tree.Single(n => n.Id == child.Id);
        childNode.ParentAreaId.ShouldBe(parent.Id);
    }

    [Fact]
    public async Task Pending_area_added_by_the_caller_is_returned_but_not_to_a_stranger()
    {
        using var author = await TestUsers.CreateAsync(Factory, Roles.User);
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Pending {Guid.NewGuid():N}", validated: false);
        area.AddedByUserId = author.UserId;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var authorTree = await author.Client.GetFromJsonAsync<List<AreaTreeNodeDto>>(
            "/api/areas/tree", TestContext.Current.CancellationToken);
        authorTree!.ShouldContain(n => n.Id == area.Id);

        var strangerTree = await stranger.Client.GetFromJsonAsync<List<AreaTreeNodeDto>>(
            "/api/areas/tree", TestContext.Current.CancellationToken);
        strangerTree!.ShouldNotContain(n => n.Id == area.Id);
    }

    [Fact]
    public async Task RouteCount_is_the_areas_own_routes_not_its_subtree()
    {
        using var owner = await TestUsers.CreateAsync(Factory, Roles.User);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var parent = await SeedData.CreateAreaAsync(db, $"Parent {Guid.NewGuid():N}");
        var child = await SeedData.CreateAreaAsync(db, $"Child {Guid.NewGuid():N}", parent);
        await SeedData.CreateRouteAsync(db, parent, owner.UserId, name: "Parent Route");
        await SeedData.CreateRouteAsync(db, child, owner.UserId, name: "Child Route 1");
        await SeedData.CreateRouteAsync(db, child, owner.UserId, name: "Child Route 2");

        var tree = await owner.Client.GetFromJsonAsync<List<AreaTreeNodeDto>>(
            "/api/areas/tree", TestContext.Current.CancellationToken);

        tree!.Single(n => n.Id == parent.Id).RouteCount.ShouldBe(1);
        tree.Single(n => n.Id == child.Id).RouteCount.ShouldBe(2);
    }

    [Fact]
    public async Task RouteCount_excludes_a_route_the_caller_cannot_see()
    {
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        using var otherUser = await TestUsers.CreateAsync(Factory, Roles.User);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        await SeedData.CreateRouteAsync(db, area, otherUser.UserId, name: "Pending Route", validated: false);

        var tree = await stranger.Client.GetFromJsonAsync<List<AreaTreeNodeDto>>(
            "/api/areas/tree", TestContext.Current.CancellationToken);

        tree!.Single(n => n.Id == area.Id).RouteCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetAreaTree_issues_exactly_two_sql_statements_regardless_of_seed_size()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connectionString = db.Database.GetConnectionString();

        using var owner = await TestUsers.CreateAsync(Factory, Roles.User);
        var root = await SeedData.CreateAreaAsync(db, $"Root {Guid.NewGuid():N}");
        for (var i = 0; i < 5; i++)
        {
            var child = await SeedData.CreateAreaAsync(db, $"Child {i} {Guid.NewGuid():N}", root);
            await SeedData.CreateRouteAsync(db, child, owner.UserId, name: $"Route {i}");
        }

        var interceptor = new CommandCountInterceptor();
        await using var countedFactory = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(connectionString).AddInterceptors(interceptor))));

        using var caller = await TestUsers.CreateAsync(countedFactory, Roles.User);
        interceptor.Count = 0;

        var response = await caller.Client.GetAsync("/api/areas/tree", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        interceptor.Count.ShouldBe(2);
    }

    private sealed class CommandCountInterceptor : DbCommandInterceptor
    {
        public int Count;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref Count);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
