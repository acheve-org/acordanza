using System.Net;
using System.Net.Http.Json;
using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Acordanza.Api.Dtos;
using Acordanza.Core.Models;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
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
    private sealed record ErrorResponse(string Error);

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
    public async Task Orientation_with_two_values_returns_their_union()
    {
        await SeedAsync();

        (await GetRouteNamesAsync("?orientation=north&orientation=south")).ShouldBe([Alpha, Beta], ignoreOrder: true);
    }

    [Fact]
    public async Task Orientation_with_an_unknown_value_returns_400()
    {
        await SeedAsync();

        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var response = await user.Client.GetAsync(
            "/api/routes?orientation=north&orientation=nowhere", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe("la orientación no es válida");
    }

    [Fact]
    public async Task No_orientation_parameter_filters_nothing()
    {
        await SeedAsync();

        (await GetRouteNamesAsync("")).ShouldBe([Alpha, Beta, Gamma], ignoreOrder: true);
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

    // Plan 34: "none" is the "Sin grado" sentinel — a gradeless route seeded alongside the
    // three graded fixture routes must be the only one it returns.
    [Fact]
    public async Task Grade_none_returns_only_gradeless_routes()
    {
        await SeedAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);
        var root = await db.Areas.FirstAsync(a => a.Id == _rootAreaId, TestContext.Current.CancellationToken);
        await SeedData.CreateRouteAsync(db, root, admin.UserId, name: "Ungraded Route", grade: null);

        (await GetRouteNamesAsync("?grade=none")).ShouldBe(["Ungraded Route"]);
    }

    [Fact]
    public async Task AidGrade_filters_exactly()
    {
        await SeedAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);
        var root = await db.Areas.FirstAsync(a => a.Id == _rootAreaId, TestContext.Current.CancellationToken);
        await SeedData.CreateRouteAsync(db, root, admin.UserId, name: "Aid Route", grade: null, aidGrade: "A1");

        (await GetRouteNamesAsync("?aidGrade=A1")).ShouldBe(["Aid Route"]);
    }

    // GET /routes/grades must omit nulls, or the filter dropdown grows a blank entry.
    [Fact]
    public async Task GetGrades_omits_nulls()
    {
        await SeedAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);
        var root = await db.Areas.FirstAsync(a => a.Id == _rootAreaId, TestContext.Current.CancellationToken);
        await SeedData.CreateRouteAsync(db, root, admin.UserId, name: "Ungraded Route", grade: null);

        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var grades = await user.Client.GetFromJsonAsync<List<string>>(
            "/api/routes/grades", TestContext.Current.CancellationToken);

        grades.ShouldNotBeNull();
        grades!.ShouldNotContain((string?)null!);
        grades.ShouldBe(["6a", "6b"], ignoreOrder: true);
    }

    // GET /routes/aid-grades mirrors GetGrades over AidGrade — [] when nothing has one,
    // and the aid grades in use once something does.
    [Fact]
    public async Task GetAidGrades_returns_empty_then_the_aid_grades_in_use()
    {
        await SeedAsync();

        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var beforeAid = await user.Client.GetFromJsonAsync<List<string>>(
            "/api/routes/aid-grades", TestContext.Current.CancellationToken);
        beforeAid.ShouldBeEmpty();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);
        var root = await db.Areas.FirstAsync(a => a.Id == _rootAreaId, TestContext.Current.CancellationToken);
        await SeedData.CreateRouteAsync(db, root, admin.UserId, name: "Aid Route", grade: null, aidGrade: "A1");

        var afterAid = await user.Client.GetFromJsonAsync<List<string>>(
            "/api/routes/aid-grades", TestContext.Current.CancellationToken);
        afterAid.ShouldBe(["A1"]);
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
