using System.Net.Http.Json;
using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Acordanza.Api.Dtos;
using Acordanza.Core.Models;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// GET /api/routes' filters (RoutesController.GetRoutes), each in isolation and a couple
// in combination — see plan 08 slice 2. Fixture: a two-level area tree (Root > Child)
// with three routes spread across it, chosen so every filter has a route it must
// include and a route it must exclude.
public class RouteFiltersTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Alpha = "Alpha Crack";
    private const string Beta = "Beta Wall";
    private const string Gamma = "Gamma Face";

    private int _rootAreaId;
    private int _childAreaId;
    private int _alphaFirstAscensionistId;

    private async Task SeedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);

        var root = await SeedData.CreateAreaAsync(db, "Root");
        var child = await SeedData.CreateAreaAsync(db, "Child", parent: root);
        _rootAreaId = root.Id;
        _childAreaId = child.Id;

        var alphaOpener = await SeedData.CreateFirstAscensionistAsync(db, "Alpha Opener");
        _alphaFirstAscensionistId = alphaOpener.Id;

        await SeedData.CreateRouteAsync(db, root, admin.UserId,
            name: Alpha, grade: "6a", orientation: "north", gear: "bolted",
            description: "a nice line up the wall",
            firstAscensionists: [alphaOpener], hasTopo: true);

        await SeedData.CreateRouteAsync(db, child, admin.UserId,
            name: Beta, grade: "6b", orientation: "south", gear: "removable",
            hasTopo: false);

        await SeedData.CreateRouteAsync(db, root, admin.UserId,
            name: Gamma, grade: "6a", orientation: null, gear: null,
            hasTopo: false);
    }

    private async Task<List<string>> GetRouteNamesAsync(string query)
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var routes = await user.Client.GetFromJsonAsync<List<RouteDto>>(
            $"/api/routes{query}", TestContext.Current.CancellationToken);
        return routes!.Select(r => r.Name).ToList();
    }

    [Fact]
    public async Task Q_filters_by_name_or_description_substring()
    {
        await SeedAsync();

        (await GetRouteNamesAsync("?q=Alpha")).ShouldBe([Alpha]);
        (await GetRouteNamesAsync("?q=nice+line")).ShouldBe([Alpha]);
    }

    [Fact]
    public async Task AreaId_without_exactArea_expands_to_the_subtree()
    {
        await SeedAsync();

        var namesUnderRoot = await GetRouteNamesAsync($"?areaId={_rootAreaId}");
        namesUnderRoot.ShouldBe([Alpha, Beta, Gamma], ignoreOrder: true);

        var namesUnderChild = await GetRouteNamesAsync($"?areaId={_childAreaId}");
        namesUnderChild.ShouldBe([Beta]);
    }

    [Fact]
    public async Task AreaId_with_exactArea_returns_only_that_areas_own_routes()
    {
        await SeedAsync();

        var namesUnderRoot = await GetRouteNamesAsync($"?areaId={_rootAreaId}&exactArea=true");
        namesUnderRoot.ShouldBe([Alpha, Gamma], ignoreOrder: true);
    }

    [Fact]
    public async Task Grade_filters_exactly()
    {
        await SeedAsync();

        (await GetRouteNamesAsync("?grade=6a")).ShouldBe([Alpha, Gamma], ignoreOrder: true);
        (await GetRouteNamesAsync("?grade=6b")).ShouldBe([Beta]);
    }

    [Fact]
    public async Task Orientation_filters_exactly()
    {
        await SeedAsync();

        (await GetRouteNamesAsync("?orientation=north")).ShouldBe([Alpha]);
    }

    [Fact]
    public async Task Gear_filters_exactly()
    {
        await SeedAsync();

        (await GetRouteNamesAsync("?gear=bolted")).ShouldBe([Alpha]);
    }

    [Fact]
    public async Task HasTopo_filters_to_routes_with_at_least_one_topo()
    {
        await SeedAsync();

        (await GetRouteNamesAsync("?hasTopo=true")).ShouldBe([Alpha]);
    }

    [Fact]
    public async Task FirstAscensionistId_filters_to_routes_they_opened()
    {
        await SeedAsync();

        (await GetRouteNamesAsync($"?firstAscensionistId={_alphaFirstAscensionistId}")).ShouldBe([Alpha]);
    }

    [Fact]
    public async Task Filters_combine_with_AND_semantics()
    {
        await SeedAsync();

        // Both routes under Root match grade=6a alone; adding orientation=north narrows
        // to just Alpha (Gamma has no orientation).
        (await GetRouteNamesAsync($"?areaId={_rootAreaId}&grade=6a&orientation=north")).ShouldBe([Alpha]);

        // grade=6a alone matches Alpha and Gamma, both under Root; scoping to the Child
        // subtree (which has neither) narrows to nothing.
        (await GetRouteNamesAsync($"?grade=6a&areaId={_childAreaId}")).ShouldBeEmpty();
    }
}
