using System.Net;
using System.Net.Http.Json;
using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// Issue #152: the backend accepts only the French sport-grade scale, enforced by
// Models/GradeValidator on write. Real HTTP, so the 400 body (what the user actually
// sees) is under test too, not just the status code.
[Trait("Category", "Integration")]
public class RouteGradeTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record CreatedRouteResponse(int Id);
    private sealed record ErrorResponse(string Error);

    private const string ExpectedError = "el grado debe seguir la escala francesa (por ejemplo 6a, 7c+, 8a)";

    private async Task<int> SeedAreaAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        return area.Id;
    }

    private static object RouteBody(int areaId, string grade) => new
    {
        name = "New Route",
        areaId,
        grade,
        orientation = (string?)null,
        gear = (string?)null,
        description = (string?)null,
        length = 100,
        obligatoryGrade = (string?)null,
        recommendedGear = (string?)null,
        optionalGear = (string?)null,
        links = (List<string>?)null,
        firstAscensionistIds = (List<int>?)null
    };

    [Theory]
    [InlineData("V+")]
    [InlineData("6d")]
    [InlineData("10a")]
    [InlineData("6")]
    public async Task POST_with_a_non_french_grade_is_rejected_with_the_spanish_message(string grade)
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, grade), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe(ExpectedError);
    }

    [Fact]
    public async Task POST_normalizes_grade_case_and_the_get_reflects_it()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, "6A"), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var detail = await user.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{created!.Id}", TestContext.Current.CancellationToken);
        detail!.Grade.ShouldBe("6a");
    }

    [Fact]
    public async Task PUT_with_an_invalid_grade_is_rejected()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        var areaId = await SeedAreaAsync();

        var createResponse = await moderator.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, "6a"), TestContext.Current.CancellationToken);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var putResponse = await moderator.Client.PutAsJsonAsync(
            $"/api/routes/{created!.Id}", RouteBody(areaId, "V+"), TestContext.Current.CancellationToken);

        putResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // Plan 34: grade is no longer required, so a blank grade on PUT is a legal "clear the
    // grade" edit, not a rejection — inverted from the pre-plan-34 assertion.
    [Fact]
    public async Task PUT_with_a_blank_grade_is_accepted_and_clears_the_grade()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        var areaId = await SeedAreaAsync();

        var createResponse = await moderator.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, "6a"), TestContext.Current.CancellationToken);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var putResponse = await moderator.Client.PutAsJsonAsync(
            $"/api/routes/{created!.Id}", RouteBody(areaId, ""), TestContext.Current.CancellationToken);
        putResponse.EnsureSuccessStatusCode();

        var detail = await moderator.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{created.Id}", TestContext.Current.CancellationToken);
        detail!.Grade.ShouldBeNull();
    }

    // Same inversion on the create side: a blank grade at POST time is legal now, and the
    // route reads back with a null grade rather than 400ing.
    [Fact]
    public async Task POST_with_a_blank_grade_is_accepted_and_reads_back_null()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, ""), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var detail = await user.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{created!.Id}", TestContext.Current.CancellationToken);
        detail!.Grade.ShouldBeNull();
    }

    private static object RouteBodyWithAidGrade(int areaId, string? grade, string? aidGrade) => new
    {
        name = "New Route",
        areaId,
        grade,
        aidGrade,
        orientation = (string?)null,
        gear = (string?)null,
        description = (string?)null,
        length = 100,
        obligatoryGrade = (string?)null,
        recommendedGear = (string?)null,
        optionalGear = (string?)null,
        links = (List<string>?)null,
        firstAscensionistIds = (List<int>?)null
    };

    [Fact]
    public async Task POST_normalizes_aid_grade_case()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBodyWithAidGrade(areaId, null, "a1"), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var detail = await user.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{created!.Id}", TestContext.Current.CancellationToken);
        detail!.AidGrade.ShouldBe("A1");
    }

    [Fact]
    public async Task POST_with_an_invalid_aid_grade_is_rejected_with_the_spanish_message()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBodyWithAidGrade(areaId, null, "A6"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe("el grado artificial debe ser A0–A5 o C0–C5 (por ejemplo A1, C2, A3+)");
    }

    [Fact]
    public async Task Both_grades_round_trip_on_one_route()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBodyWithAidGrade(areaId, "6c", "A1"), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var detail = await user.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{created!.Id}", TestContext.Current.CancellationToken);
        detail!.Grade.ShouldBe("6c");
        detail.AidGrade.ShouldBe("A1");
    }

    [Fact]
    public async Task GET_filtered_by_grade_matches_a_route_created_with_a_different_case()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var createResponse = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, "6A"), TestContext.Current.CancellationToken);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var routes = await user.Client.GetFromJsonAsync<List<RouteDto>>(
            "/api/routes?grade=6a", TestContext.Current.CancellationToken);

        routes!.ShouldContain(r => r.Id == created!.Id);
    }
}
