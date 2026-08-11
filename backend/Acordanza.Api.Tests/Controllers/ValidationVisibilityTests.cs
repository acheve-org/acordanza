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

// Plan 11 (.claude/plans/11-show-only-validated-items.md): validation is a publication
// gate on list/browse endpoints outside "Mi acordanza" — visible(x) ⟺ Approved || mine
// (Models/ValidationVisibility.cs). Detail-by-id endpoints stay open regardless of status
// (§2 of the plan) — that boundary is exercised here too, not just the list side.
public class ValidationVisibilityTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    // ---- Routes ----

    [Fact]
    public async Task Approved_route_is_visible_to_a_stranger()
    {
        using var author = await TestUsers.CreateAsync(Factory, Roles.User);
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        var routeId = await SeedRouteAsync(author.UserId, validated: true);

        var routes = await stranger.Client.GetFromJsonAsync<List<RouteDto>>(
            "/api/routes", TestContext.Current.CancellationToken);

        routes!.ShouldContain(r => r.Id == routeId);
    }

    [Fact]
    public async Task Pending_route_is_hidden_from_a_stranger_but_visible_to_its_author()
    {
        using var author = await TestUsers.CreateAsync(Factory, Roles.User);
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        var routeId = await SeedRouteAsync(author.UserId, validated: false);

        var strangerRoutes = await stranger.Client.GetFromJsonAsync<List<RouteDto>>(
            "/api/routes", TestContext.Current.CancellationToken);
        strangerRoutes!.ShouldNotContain(r => r.Id == routeId);

        var authorRoutes = await author.Client.GetFromJsonAsync<List<RouteDto>>(
            "/api/routes", TestContext.Current.CancellationToken);
        authorRoutes!.ShouldContain(r => r.Id == routeId);
    }

    [Fact]
    public async Task Rejected_route_is_hidden_from_a_stranger_but_visible_to_its_author()
    {
        using var author = await TestUsers.CreateAsync(Factory, Roles.User);
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        int routeId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
            var route = await SeedData.CreateRouteAsync(db, area, author.UserId, validated: false);
            Validation.MarkRejected(route, author.UserId, "no cumple los requisitos");
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            routeId = route.Id;
        }

        var strangerRoutes = await stranger.Client.GetFromJsonAsync<List<RouteDto>>(
            "/api/routes", TestContext.Current.CancellationToken);
        strangerRoutes!.ShouldNotContain(r => r.Id == routeId);

        var authorRoutes = await author.Client.GetFromJsonAsync<List<RouteDto>>(
            "/api/routes", TestContext.Current.CancellationToken);
        authorRoutes!.ShouldContain(r => r.Id == routeId);
    }

    [Fact]
    public async Task Pending_route_detail_stays_reachable_by_id_to_a_stranger()
    {
        using var author = await TestUsers.CreateAsync(Factory, Roles.User);
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        var routeId = await SeedRouteAsync(author.UserId, validated: false);

        var response = await stranger.Client.GetAsync($"/api/routes/{routeId}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenedByMe_bypasses_visibility_for_the_callers_own_pending_opening()
    {
        using var opener = await TestUsers.CreateFirstAscensionistLinkedAsync(Factory, "Pending Opener");
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fa = await db.FirstAscensionists.SingleAsync(
            f => f.UserId == opener.UserId, TestContext.Current.CancellationToken);
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        // Created by someone else and never validated — opener should still see it via
        // openedByMe, since they're credited on the route regardless of who created it.
        using var creator = await TestUsers.CreateAsync(Factory, Roles.User);
        var route = await SeedData.CreateRouteAsync(db, area, creator.UserId, validated: false, firstAscensionists: [fa]);

        var ownOpenings = await opener.Client.GetFromJsonAsync<List<RouteDto>>(
            "/api/routes?openedByMe=true", TestContext.Current.CancellationToken);
        ownOpenings!.ShouldContain(r => r.Id == route.Id);

        var regularList = await opener.Client.GetFromJsonAsync<List<RouteDto>>(
            "/api/routes", TestContext.Current.CancellationToken);
        regularList!.ShouldNotContain(r => r.Id == route.Id);
    }

    // ---- Routes: GET /routes/pending ----

    [Fact]
    public async Task GetPendingRoutes_is_area_scoped_for_an_area_expert()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownArea = await SeedData.CreateAreaAsync(db, $"Own Area {Guid.NewGuid():N}");
        var otherArea = await SeedData.CreateAreaAsync(db, $"Other Area {Guid.NewGuid():N}");

        using var expert = await TestUsers.CreateAreaExpertAsync(Factory, ownArea.Id);
        using var creator = await TestUsers.CreateAsync(Factory, Roles.User);

        var ownRoute = await SeedData.CreateRouteAsync(db, ownArea, creator.UserId, name: "Own Area Route", validated: false);
        var otherRoute = await SeedData.CreateRouteAsync(db, otherArea, creator.UserId, name: "Other Area Route", validated: false);

        var pending = await expert.Client.GetFromJsonAsync<List<RoutePendingDto>>(
            "/api/routes/pending", TestContext.Current.CancellationToken);

        pending!.ShouldContain(r => r.Id == ownRoute.Id);
        pending!.ShouldNotContain(r => r.Id == otherRoute.Id);
    }

    [Fact]
    public async Task GetPendingRoutes_is_empty_for_a_caller_without_validate_rights()
    {
        using var plain = await TestUsers.CreateAsync(Factory, Roles.User);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        await SeedData.CreateRouteAsync(db, area, plain.UserId, validated: false);

        var pending = await plain.Client.GetFromJsonAsync<List<RoutePendingDto>>(
            "/api/routes/pending", TestContext.Current.CancellationToken);

        pending.ShouldBeEmpty();
    }

    // ---- Areas ----

    [Fact]
    public async Task Approved_area_is_visible_to_a_stranger()
    {
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Approved Area {Guid.NewGuid():N}", validated: true);

        var areas = await stranger.Client.GetFromJsonAsync<List<AreaDto>>(
            "/api/areas", TestContext.Current.CancellationToken);

        areas!.ShouldContain(a => a.Id == area.Id);
    }

    [Fact]
    public async Task Pending_area_is_hidden_from_a_stranger_but_visible_to_its_author()
    {
        using var author = await TestUsers.CreateAsync(Factory, Roles.User);
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Pending Area {Guid.NewGuid():N}", validated: false);
        area.AddedByUserId = author.UserId;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var strangerAreas = await stranger.Client.GetFromJsonAsync<List<AreaDto>>(
            "/api/areas", TestContext.Current.CancellationToken);
        strangerAreas!.ShouldNotContain(a => a.Id == area.Id);

        var authorAreas = await author.Client.GetFromJsonAsync<List<AreaDto>>(
            "/api/areas", TestContext.Current.CancellationToken);
        authorAreas!.ShouldContain(a => a.Id == area.Id);
    }

    [Fact]
    public async Task IncludeUnvalidated_returns_every_area_for_a_moderator()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pendingArea = await SeedData.CreateAreaAsync(db, $"Pending Area {Guid.NewGuid():N}", validated: false);

        var areas = await moderator.Client.GetFromJsonAsync<List<AreaDto>>(
            "/api/areas?includeUnvalidated=true", TestContext.Current.CancellationToken);

        areas!.ShouldContain(a => a.Id == pendingArea.Id);
    }

    [Fact]
    public async Task IncludeUnvalidated_403s_for_a_plain_user_on_areas()
    {
        using var plain = await TestUsers.CreateAsync(Factory, Roles.User);

        var response = await plain.Client.GetAsync("/api/areas?includeUnvalidated=true", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetPendingAreas_403s_without_area_validate()
    {
        using var plain = await TestUsers.CreateAsync(Factory, Roles.User);

        var response = await plain.Client.GetAsync("/api/areas/pending", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetPendingAreas_returns_pending_areas_for_a_moderator()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pendingArea = await SeedData.CreateAreaAsync(db, $"Pending Area {Guid.NewGuid():N}", validated: false);

        var pending = await moderator.Client.GetFromJsonAsync<List<AreaPendingDto>>(
            "/api/areas/pending", TestContext.Current.CancellationToken);

        pending!.ShouldContain(a => a.Id == pendingArea.Id);
    }

    // ---- First ascensionists ----

    [Fact]
    public async Task Approved_first_ascensionist_is_visible_to_a_stranger()
    {
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fa = await SeedData.CreateFirstAscensionistAsync(db, $"Approved Opener {Guid.NewGuid():N}", validated: true);

        var openers = await stranger.Client.GetFromJsonAsync<List<FirstAscensionistDto>>(
            "/api/first-ascensionists", TestContext.Current.CancellationToken);

        openers!.ShouldContain(o => o.Id == fa.Id);
    }

    [Fact]
    public async Task Pending_first_ascensionist_is_hidden_from_a_stranger_but_visible_to_its_author()
    {
        using var author = await TestUsers.CreateAsync(Factory, Roles.User);
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fa = await SeedData.CreateFirstAscensionistAsync(db, $"Pending Opener {Guid.NewGuid():N}", validated: false);
        fa.AddedByUserId = author.UserId;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var strangerOpeners = await stranger.Client.GetFromJsonAsync<List<FirstAscensionistDto>>(
            "/api/first-ascensionists", TestContext.Current.CancellationToken);
        strangerOpeners!.ShouldNotContain(o => o.Id == fa.Id);

        var authorOpeners = await author.Client.GetFromJsonAsync<List<FirstAscensionistDto>>(
            "/api/first-ascensionists", TestContext.Current.CancellationToken);
        authorOpeners!.ShouldContain(o => o.Id == fa.Id);
    }

    [Fact]
    public async Task Pending_first_ascensionist_is_hidden_from_profiles_for_a_stranger()
    {
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fa = await SeedData.CreateFirstAscensionistAsync(db, $"Pending Opener {Guid.NewGuid():N}", validated: false);

        var profiles = await stranger.Client.GetFromJsonAsync<List<FirstAscensionistProfileDto>>(
            "/api/first-ascensionists/profiles", TestContext.Current.CancellationToken);

        profiles!.ShouldNotContain(p => p.Id == fa.Id);
    }

    [Fact]
    public async Task IncludeUnvalidated_returns_every_first_ascensionist_for_a_moderator()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = await SeedData.CreateFirstAscensionistAsync(db, $"Pending Opener {Guid.NewGuid():N}", validated: false);

        var openers = await moderator.Client.GetFromJsonAsync<List<FirstAscensionistDto>>(
            "/api/first-ascensionists?includeUnvalidated=true", TestContext.Current.CancellationToken);

        openers!.ShouldContain(o => o.Id == pending.Id);
    }

    [Fact]
    public async Task IncludeUnvalidated_403s_for_a_plain_user_on_first_ascensionists()
    {
        using var plain = await TestUsers.CreateAsync(Factory, Roles.User);

        var response = await plain.Client.GetAsync(
            "/api/first-ascensionists?includeUnvalidated=true", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<int> SeedRouteAsync(string authorUserId, bool validated)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, authorUserId, name: $"Route {Guid.NewGuid():N}", validated: validated);
        return route.Id;
    }
}
