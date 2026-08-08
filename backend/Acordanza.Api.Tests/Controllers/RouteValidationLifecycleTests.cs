using System.Net.Http.Json;
using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// A PUT that changes nothing must keep DecidedAt/DecidedByUserId; a change to any
// individual field must clear them — Models/Validation.cs's shared lifecycle, applied
// by RoutesController.UpdateRoute via Validation.InvalidateIfChanged. The
// FirstAscensionistIds set is compared order-insensitively (SetEquals, see
// RoutesController), which is the subtle case plan 08 slice 4 calls out — reordering
// the same set of openers must NOT invalidate. See plan 03's original rule.
public class RouteValidationLifecycleTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record Scenario(TestUser Moderator, int RouteId, int OtherAreaId, int Fa1Id, int Fa2Id, int Fa3Id) : IDisposable
    {
        public void Dispose() => Moderator.Dispose();
    }

    private async Task<Scenario> SeedValidatedRouteAsync()
    {
        var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var area = await SeedData.CreateAreaAsync(db, "Base Area");
        var otherArea = await SeedData.CreateAreaAsync(db, "Other Area");
        var fa1 = await SeedData.CreateFirstAscensionistAsync(db, "Opener One");
        var fa2 = await SeedData.CreateFirstAscensionistAsync(db, "Opener Two");
        var fa3 = await SeedData.CreateFirstAscensionistAsync(db, "Opener Three");

        var route = await SeedData.CreateRouteAsync(
            db, area, moderator.UserId,
            name: "Base Route", grade: "6a", orientation: "north", gear: "bolted",
            description: "base desc", tags: "tag1,tag2",
            firstAscensionists: [fa1, fa2]);

        route.ValidationStatus = ReviewStatus.Approved;
        route.DecidedAt = DateTimeOffset.UtcNow;
        route.DecidedByUserId = moderator.UserId;
        await db.SaveChangesAsync();

        return new Scenario(moderator, route.Id, otherArea.Id, fa1.Id, fa2.Id, fa3.Id);
    }

    private static UpsertRouteRequest BaseRequest(Scenario s, int areaId) => new(
        "Base Route", areaId, "6a", "north", "bolted", "base desc", "tag1,tag2", [s.Fa1Id, s.Fa2Id]);

    private async Task<DateTimeOffset?> PutAndGetValidatedAtAsync(Scenario s, UpsertRouteRequest request)
    {
        var putResponse = await s.Moderator.Client.PutAsJsonAsync(
            $"/api/routes/{s.RouteId}", request, TestContext.Current.CancellationToken);
        putResponse.EnsureSuccessStatusCode();

        var detail = await s.Moderator.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{s.RouteId}", TestContext.Current.CancellationToken);
        return detail!.DecidedAt;
    }

    [Fact]
    public async Task No_op_PUT_keeps_validation()
    {
        using var s = await SeedValidatedRouteAsync();
        var areaId = await AreaIdAsync(s);

        var validatedAt = await PutAndGetValidatedAtAsync(s, BaseRequest(s, areaId));

        validatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Changing_the_name_clears_validation()
    {
        using var s = await SeedValidatedRouteAsync();
        var areaId = await AreaIdAsync(s);
        var request = BaseRequest(s, areaId) with { Name = "Renamed Route" };

        (await PutAndGetValidatedAtAsync(s, request)).ShouldBeNull();
    }

    [Fact]
    public async Task Changing_the_area_clears_validation()
    {
        using var s = await SeedValidatedRouteAsync();
        var request = BaseRequest(s, s.OtherAreaId);

        (await PutAndGetValidatedAtAsync(s, request)).ShouldBeNull();
    }

    [Fact]
    public async Task Changing_the_grade_clears_validation()
    {
        using var s = await SeedValidatedRouteAsync();
        var areaId = await AreaIdAsync(s);
        var request = BaseRequest(s, areaId) with { Grade = "7a" };

        (await PutAndGetValidatedAtAsync(s, request)).ShouldBeNull();
    }

    [Fact]
    public async Task Changing_the_orientation_clears_validation()
    {
        using var s = await SeedValidatedRouteAsync();
        var areaId = await AreaIdAsync(s);
        var request = BaseRequest(s, areaId) with { Orientation = "south" };

        (await PutAndGetValidatedAtAsync(s, request)).ShouldBeNull();
    }

    [Fact]
    public async Task Changing_the_gear_clears_validation()
    {
        using var s = await SeedValidatedRouteAsync();
        var areaId = await AreaIdAsync(s);
        var request = BaseRequest(s, areaId) with { Gear = "removable" };

        (await PutAndGetValidatedAtAsync(s, request)).ShouldBeNull();
    }

    [Fact]
    public async Task Changing_the_description_clears_validation()
    {
        using var s = await SeedValidatedRouteAsync();
        var areaId = await AreaIdAsync(s);
        var request = BaseRequest(s, areaId) with { Description = "a different description" };

        (await PutAndGetValidatedAtAsync(s, request)).ShouldBeNull();
    }

    [Fact]
    public async Task Changing_the_tags_clears_validation()
    {
        using var s = await SeedValidatedRouteAsync();
        var areaId = await AreaIdAsync(s);
        var request = BaseRequest(s, areaId) with { Tags = "tag1,tag2,tag3" };

        (await PutAndGetValidatedAtAsync(s, request)).ShouldBeNull();
    }

    [Fact]
    public async Task Changing_the_first_ascensionist_set_clears_validation()
    {
        using var s = await SeedValidatedRouteAsync();
        var areaId = await AreaIdAsync(s);
        var request = BaseRequest(s, areaId) with { FirstAscensionistIds = [s.Fa1Id, s.Fa2Id, s.Fa3Id] };

        (await PutAndGetValidatedAtAsync(s, request)).ShouldBeNull();
    }

    [Fact]
    public async Task Reordering_the_same_first_ascensionist_set_keeps_validation()
    {
        using var s = await SeedValidatedRouteAsync();
        var areaId = await AreaIdAsync(s);
        var request = BaseRequest(s, areaId) with { FirstAscensionistIds = [s.Fa2Id, s.Fa1Id] };

        (await PutAndGetValidatedAtAsync(s, request)).ShouldNotBeNull();
    }

    private async Task<int> AreaIdAsync(Scenario s)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var route = await db.Routes.FindAsync([s.RouteId], TestContext.Current.CancellationToken);
        return route!.AreaId;
    }

    // Plan 11 §4: routes now auto-validate on create for a creator who can validate
    // them — the same rule AreasController.CreateArea/FirstAscensionistsController.
    // CreateFirstAscensionist already applied, closing the gap where a moderator's own
    // new route used to stay Pending (and so, once validation gates visibility,
    // would vanish from "Busca" the instant they saved it).

    [Fact]
    public async Task Moderator_created_route_is_auto_validated()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");

        var request = new UpsertRouteRequest("New Route", area.Id, "6a", null, null, null, null, []);
        var response = await moderator.Client.PostAsJsonAsync("/api/routes", request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var detail = await moderator.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{created!.Id}", TestContext.Current.CancellationToken);
        detail!.ValidationStatus.ShouldBe("approved");
        detail.DecidedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Plain_user_created_route_stays_pending()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");

        var request = new UpsertRouteRequest("New Route", area.Id, "6a", null, null, null, null, []);
        var response = await user.Client.PostAsJsonAsync("/api/routes", request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var detail = await user.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{created!.Id}", TestContext.Current.CancellationToken);
        detail!.ValidationStatus.ShouldBe("pending");
        detail.DecidedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Area_experts_route_within_their_own_area_is_auto_validated()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        using var expert = await TestUsers.CreateAreaExpertAsync(Factory, area.Id);

        var request = new UpsertRouteRequest("New Route", area.Id, "6a", null, null, null, null, []);
        var response = await expert.Client.PostAsJsonAsync("/api/routes", request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var detail = await expert.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{created!.Id}", TestContext.Current.CancellationToken);
        detail!.ValidationStatus.ShouldBe("approved");
    }

    [Fact]
    public async Task Area_experts_route_outside_their_area_stays_pending()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownArea = await SeedData.CreateAreaAsync(db, $"Own Area {Guid.NewGuid():N}");
        var otherArea = await SeedData.CreateAreaAsync(db, $"Other Area {Guid.NewGuid():N}");
        using var expert = await TestUsers.CreateAreaExpertAsync(Factory, ownArea.Id);

        var request = new UpsertRouteRequest("New Route", otherArea.Id, "6a", null, null, null, null, []);
        var response = await expert.Client.PostAsJsonAsync("/api/routes", request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var detail = await expert.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{created!.Id}", TestContext.Current.CancellationToken);
        detail!.ValidationStatus.ShouldBe("pending");
    }

    private sealed record CreatedRouteResponse(int Id);
}
