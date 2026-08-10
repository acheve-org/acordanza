using System.Net;
using System.Net.Http.Json;
using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Authorization;

// AreaExpertAuthorizer.CanActOnAreaAsync (see Authorization/AreaExpertAuthorizer.cs) has
// two branches, both exercised here through RoutesController.ValidateRoute/RejectRoute
// (neither is policy-gated — see AuthorizationMatrixTests' comment on why those two
// endpoints are deliberately absent from the matrix): a bare route.validate claim
// short-circuits to allowed for any area; absent that claim, an approved AreaExpert is
// allowed only within the top-level area (and its subareas) they were approved for.
public class AreaExpertScopingTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Area_expert_can_validate_a_route_in_a_nested_subarea_of_their_own_area()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var topArea = await SeedData.CreateAreaAsync(db, $"Top {Guid.NewGuid():N}");
        var subArea = await SeedData.CreateAreaAsync(db, "Sub Area", parent: topArea);
        using var expert = await TestUsers.CreateAreaExpertAsync(Factory, topArea.Id);
        var route = await SeedData.CreateRouteAsync(db, subArea, expert.UserId, validated: false);

        var response = await expert.Client.PostAsync(
            $"/api/routes/{route.Id}/validate", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Area_expert_cannot_validate_a_route_in_a_different_tree()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownArea = await SeedData.CreateAreaAsync(db, $"Own {Guid.NewGuid():N}");
        var otherArea = await SeedData.CreateAreaAsync(db, $"Other {Guid.NewGuid():N}");
        using var expert = await TestUsers.CreateAreaExpertAsync(Factory, ownArea.Id);
        var route = await SeedData.CreateRouteAsync(db, otherArea, expert.UserId, validated: false);

        var response = await expert.Client.PostAsync(
            $"/api/routes/{route.Id}/validate", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Plain_route_validate_claim_holder_can_validate_in_any_area()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, moderator.UserId, validated: false);

        var response = await moderator.Client.PostAsync(
            $"/api/routes/{route.Id}/validate", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Area_expert_can_reject_a_route_within_their_own_area()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        using var expert = await TestUsers.CreateAreaExpertAsync(Factory, area.Id);
        var route = await SeedData.CreateRouteAsync(db, area, expert.UserId, validated: false);

        var response = await expert.Client.PostAsJsonAsync(
            $"/api/routes/{route.Id}/reject", new RejectValidationRequest("no cumple criterios"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Area_expert_cannot_reject_a_route_outside_their_own_area()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownArea = await SeedData.CreateAreaAsync(db, $"Own {Guid.NewGuid():N}");
        var otherArea = await SeedData.CreateAreaAsync(db, $"Other {Guid.NewGuid():N}");
        using var expert = await TestUsers.CreateAreaExpertAsync(Factory, ownArea.Id);
        var route = await SeedData.CreateRouteAsync(db, otherArea, expert.UserId, validated: false);

        var response = await expert.Client.PostAsJsonAsync(
            $"/api/routes/{route.Id}/reject", new RejectValidationRequest("no cumple criterios"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
