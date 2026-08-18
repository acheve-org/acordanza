using System.Net;
using System.Net.Http.Json;
using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// ClimbingRoute.Slug (plan 21): GenerateUniqueRouteSlugAsync's "-2"/"-3" suffix
// disambiguation scoped per-area (not global, unlike FirstAscensionist.Slug — see
// ClimbingRoute.Slug's doc comment), regeneration on rename/area-move, and
// GET /routes/by-slug-path/{*path} resolution. All requests run as a moderator so
// creates auto-validate.
public class RouteSlugTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private static UpsertRouteRequest UpsertRequest(string name, int areaId, List<int>? firstAscensionistIds = null) =>
        new(name, areaId, "6a", null, null, null, null, 100, null, null, null, null, firstAscensionistIds);

    private async Task<(int Id, string Slug)> CreateAsync(TestUser user, string name, int areaId)
    {
        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", UpsertRequest(name, areaId), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = await response.Content.ReadFromJsonAsync<CreateRouteResponse>(TestContext.Current.CancellationToken);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var slug = await db.Routes.Where(r => r.Id == created!.Id).Select(r => r.Slug).SingleAsync(TestContext.Current.CancellationToken);

        return (created!.Id, slug);
    }

    private sealed record CreateRouteResponse(int Id);

    [Fact]
    public async Task Creating_two_routes_with_the_same_name_in_the_same_area_disambiguates_the_slug()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, "Riglos");

        var first = await CreateAsync(moderator, "Directa", area.Id);
        var second = await CreateAsync(moderator, "Directa", area.Id);

        first.Slug.ShouldBe("directa");
        second.Slug.ShouldBe("directa-2");
    }

    [Fact]
    public async Task The_same_name_in_a_different_area_is_not_suffixed()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var areaA = await SeedData.CreateAreaAsync(db, "Riglos");
        var areaB = await SeedData.CreateAreaAsync(db, "Mallos de Agüero");

        var inA = await CreateAsync(moderator, "Directa", areaA.Id);
        var inB = await CreateAsync(moderator, "Directa", areaB.Id);

        inA.Slug.ShouldBe("directa");
        inB.Slug.ShouldBe("directa");
    }

    [Fact]
    public async Task Renaming_regenerates_the_slug_and_the_old_by_slug_path_404s()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, "Riglos");

        var created = await CreateAsync(moderator, "Fisura del Diablo", area.Id);

        var updateResponse = await moderator.Client.PutAsJsonAsync(
            $"/api/routes/{created.Id}", UpsertRequest("Fisura Renombrada", area.Id), TestContext.Current.CancellationToken);
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var newPathResponse = await moderator.Client.GetAsync(
            "/api/routes/by-slug-path/riglos/fisura-renombrada", TestContext.Current.CancellationToken);
        newPathResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var oldPathResponse = await moderator.Client.GetAsync(
            $"/api/routes/by-slug-path/riglos/{created.Slug}", TestContext.Current.CancellationToken);
        oldPathResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Moving_to_a_different_area_regenerates_the_slug_against_the_new_areas_siblings()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var origin = await SeedData.CreateAreaAsync(db, "Riglos");
        var destination = await SeedData.CreateAreaAsync(db, "Mallos de Agüero");

        // Already a "directa" in the destination area — the moved route must collide
        // with it and get suffixed, not just keep its origin-area slug.
        await CreateAsync(moderator, "Directa", destination.Id);
        var moved = await CreateAsync(moderator, "Directa", origin.Id);
        moved.Slug.ShouldBe("directa"); // unique within origin at creation time

        var updateResponse = await moderator.Client.PutAsJsonAsync(
            $"/api/routes/{moved.Id}", UpsertRequest("Directa", destination.Id), TestContext.Current.CancellationToken);
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var newSlug = await db.Routes.Where(r => r.Id == moved.Id).Select(r => r.Slug)
            .SingleAsync(TestContext.Current.CancellationToken);
        newSlug.ShouldBe("directa-2");
    }

    [Fact]
    public async Task A_no_op_resave_keeps_the_slug()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, "Riglos");

        var created = await CreateAsync(moderator, "Fisura del Diablo", area.Id);

        var updateResponse = await moderator.Client.PutAsJsonAsync(
            $"/api/routes/{created.Id}", UpsertRequest("Fisura del Diablo", area.Id), TestContext.Current.CancellationToken);
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var slugAfterResave = await db.Routes.Where(r => r.Id == created.Id).Select(r => r.Slug)
            .SingleAsync(TestContext.Current.CancellationToken);
        slugAfterResave.ShouldBe(created.Slug);
    }

    [Fact]
    public async Task By_slug_path_resolves_through_a_multi_level_area_path()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var top = await SeedData.CreateAreaAsync(db, "Galicia");
        var sub = await SeedData.CreateAreaAsync(db, "Peña Rúa", top);

        var created = await CreateAsync(moderator, "Fisura del Diablo", sub.Id);

        var response = await moderator.Client.GetFromJsonAsync<RouteDetailDto>(
            "/api/routes/by-slug-path/galicia/pena-rua/fisura-del-diablo", TestContext.Current.CancellationToken);

        response!.Id.ShouldBe(created.Id);
        response.Slug.ShouldBe("fisura-del-diablo");
        response.AreaSlugPath.ShouldBe("galicia/pena-rua");
    }

    [Fact]
    public async Task By_slug_path_404s_for_an_unknown_area_path_an_unknown_route_slug_and_a_route_slug_with_no_area_segments()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, "Riglos");
        var created = await CreateAsync(moderator, "Fisura del Diablo", area.Id);

        var unknownArea = await moderator.Client.GetAsync(
            "/api/routes/by-slug-path/no-existe/fisura-del-diablo", TestContext.Current.CancellationToken);
        unknownArea.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var unknownRoute = await moderator.Client.GetAsync(
            "/api/routes/by-slug-path/riglos/no-existe", TestContext.Current.CancellationToken);
        unknownRoute.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var noAreaSegments = await moderator.Client.GetAsync(
            $"/api/routes/by-slug-path/{created.Slug}", TestContext.Current.CancellationToken);
        noAreaSegments.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Route_list_and_detail_responses_include_slug_and_area_slug_path()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var top = await SeedData.CreateAreaAsync(db, "Galicia");
        var sub = await SeedData.CreateAreaAsync(db, "Peña Rúa", top);

        var created = await CreateAsync(moderator, "Fisura del Diablo", sub.Id);

        var list = await moderator.Client.GetFromJsonAsync<List<RouteDto>>(
            "/api/routes", TestContext.Current.CancellationToken);
        var listed = list!.Single(r => r.Id == created.Id);
        listed.Slug.ShouldBe("fisura-del-diablo");
        listed.AreaSlugPath.ShouldBe("galicia/pena-rua");

        var detail = await moderator.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{created.Id}", TestContext.Current.CancellationToken);
        detail!.Slug.ShouldBe("fisura-del-diablo");
        detail.AreaSlugPath.ShouldBe("galicia/pena-rua");
    }

    [Fact]
    public async Task Pending_route_response_includes_slug_and_area_slug_path()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var author = await TestUsers.CreateAsync(Factory, Roles.User);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, "Riglos");

        // A plain user's create is left pending (no route.validate), unlike the
        // moderator-created routes CreateAsync above relies on auto-validating.
        var response = await author.Client.PostAsJsonAsync(
            "/api/routes", UpsertRequest("Fisura del Diablo", area.Id), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = await response.Content.ReadFromJsonAsync<CreateRouteResponse>(TestContext.Current.CancellationToken);

        var pending = await moderator.Client.GetFromJsonAsync<List<RoutePendingDto>>(
            "/api/routes/pending", TestContext.Current.CancellationToken);

        var pendingRoute = pending!.Single(r => r.Id == created!.Id);
        pendingRoute.Slug.ShouldBe("fisura-del-diablo");
        pendingRoute.AreaSlugPath.ShouldBe("riglos");
    }

    [Fact]
    public async Task My_ticks_response_includes_route_slug_and_area_slug_path()
    {
        using var climber = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, "Riglos");

        var created = await CreateAsync(climber, "Fisura del Diablo", area.Id);

        var tickResponse = await climber.Client.PostAsJsonAsync(
            $"/api/routes/{created.Id}/ticks", new CreateTickRequest("2024-01-01", null, null),
            TestContext.Current.CancellationToken);
        tickResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var myTicks = await climber.Client.GetFromJsonAsync<PagedMyTicksResponse>(
            "/api/ticks/mine", TestContext.Current.CancellationToken);

        var tick = myTicks!.Items.Single(t => t.RouteId == created.Id);
        tick.RouteSlug.ShouldBe("fisura-del-diablo");
        tick.AreaSlugPath.ShouldBe("riglos");
    }
}
