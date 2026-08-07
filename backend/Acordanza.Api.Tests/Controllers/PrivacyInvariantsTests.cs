using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// Ticks are private per user and ApplicationUser.Email is PII that must never surface
// to other users (backend/CLAUDE.md's "Auth model" section) — see plan 08 slice 3.
public partial class PrivacyInvariantsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task HasTick_reflects_only_the_caller_on_the_list_endpoint()
    {
        using var owner = await TestUsers.CreateAsync(Factory, Roles.User);
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        var routeId = await SeedRouteAndTickAsync(owner.UserId);

        var ownerRoutes = await owner.Client.GetFromJsonAsync<List<RouteDto>>(
            "/api/routes", TestContext.Current.CancellationToken);
        ownerRoutes!.Single(r => r.Id == routeId).HasTick.ShouldBeTrue();

        var strangerRoutes = await stranger.Client.GetFromJsonAsync<List<RouteDto>>(
            "/api/routes", TestContext.Current.CancellationToken);
        strangerRoutes!.Single(r => r.Id == routeId).HasTick.ShouldBeFalse();
    }

    [Fact]
    public async Task HasTick_reflects_only_the_caller_on_the_detail_endpoint()
    {
        using var owner = await TestUsers.CreateAsync(Factory, Roles.User);
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        var routeId = await SeedRouteAndTickAsync(owner.UserId);

        var ownerDetail = await owner.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{routeId}", TestContext.Current.CancellationToken);
        ownerDetail!.HasTick.ShouldBeTrue();

        var strangerDetail = await stranger.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{routeId}", TestContext.Current.CancellationToken);
        strangerDetail!.HasTick.ShouldBeFalse();
    }

    [Fact]
    public async Task Route_detail_never_exposes_another_users_tick_notes()
    {
        using var owner = await TestUsers.CreateAsync(Factory, Roles.User);
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        const string secretNote = "SECRET-NOTE-only-the-owner-should-ever-see-this";
        var routeId = await SeedRouteAndTickAsync(owner.UserId, notes: secretNote);

        var response = await stranger.Client.GetAsync($"/api/routes/{routeId}", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldNotContain(secretNote);
    }

    [Fact]
    public async Task Deleting_someone_elses_tick_silently_no_ops()
    {
        using var owner = await TestUsers.CreateAsync(Factory, Roles.User);
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        int tickId;
        int routeId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var area = await SeedData.CreateAreaAsync(db, "Privacy Test Area");
            var route = await SeedData.CreateRouteAsync(db, area, owner.UserId, name: "Privacy Test Route");
            routeId = route.Id;
            var tick = await SeedData.CreateTickAsync(db, route, owner.UserId);
            tickId = tick.Id;
        }

        var response = await stranger.Client.DeleteAsync($"/api/ticks/{tickId}", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Ticks.AnyAsync(t => t.Id == tickId, TestContext.Current.CancellationToken)).ShouldBeTrue();
        }

        _ = routeId; // seeded for realism; not asserted on directly
    }

    [Fact]
    public async Task No_response_reachable_by_a_normal_user_ever_contains_an_email_address()
    {
        using var normal = await TestUsers.CreateAsync(Factory, Roles.User, name: "Normal Person");

        // A route linked to a FirstAscensionist with a linked account exercises the
        // Include(...).ThenInclude(o => o.User) path (RoutesController.GetRoutes/
        // GetRoute) — FirstAscensionistDto must expose only the linked user's Name,
        // never the ApplicationUser.Email that navigation property also carries.
        using var linkedOpenerUser = await TestUsers.CreateFirstAscensionistLinkedAsync(Factory, "Linked Opener");

        int areaId, routeId, firstAscensionistId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var opener = await db.FirstAscensionists.SingleAsync(
                f => f.Name == "Linked Opener", TestContext.Current.CancellationToken);
            var area = await SeedData.CreateAreaAsync(db, "Email Leak Check Area");
            var route = await SeedData.CreateRouteAsync(
                db, area, normal.UserId, name: "Email Leak Check Route", firstAscensionists: [opener]);
            await SeedData.CreateTickAsync(db, route, normal.UserId);

            areaId = area.Id;
            routeId = route.Id;
            firstAscensionistId = opener.Id;
        }

        // /api/auth/me is deliberately excluded: it returns the caller's own
        // email as their username, which is expected self-service, not a leak.
        var endpoints = new[]
        {
            "/api/routes",
            $"/api/routes/{routeId}",
            "/api/areas",
            $"/api/areas/{areaId}/children",
            "/api/first-ascensionists",
            "/api/first-ascensionists/profiles",
            $"/api/first-ascensionists/{firstAscensionistId}",
            "/api/ticks/mine"
        };

        foreach (var endpoint in endpoints)
        {
            var response = await normal.Client.GetAsync(endpoint, TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            EmailPattern().Matches(body).Count.ShouldBe(0, $"endpoint {endpoint} leaked an email address: {body}");
        }
    }

    private async Task<int> SeedRouteAndTickAsync(string climberUserId, string notes = "")
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, climberUserId, name: $"Route {Guid.NewGuid():N}");
        await SeedData.CreateTickAsync(db, route, climberUserId, notes: notes);
        return route.Id;
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();
}
