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

// Plan 34: the only fields RoutesController.CreateRoute/UpdateRoute still require are
// Name and AreaId — grade, aid grade and length are all optional now. Real HTTP, so both
// the 200 read-back and the 400 bodies are under test.
[Trait("Category", "Integration")]
public class RouteRequiredFieldsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record CreatedRouteResponse(int Id);
    private sealed record ErrorResponse(string Error);

    private async Task<int> SeedAreaAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        return area.Id;
    }

    [Fact]
    public async Task POST_with_only_name_and_areaId_succeeds_and_reads_back_all_nulls()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", new { name = "Minimal Route", areaId }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var detail = await user.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{created!.Id}", TestContext.Current.CancellationToken);
        detail!.Grade.ShouldBeNull();
        detail.AidGrade.ShouldBeNull();
        detail.Length.ShouldBeNull();
    }

    [Fact]
    public async Task POST_with_a_blank_name_is_rejected()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", new { name = "   ", areaId }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe("el nombre es obligatorio");
    }

    [Fact]
    public async Task POST_with_areaId_zero_is_rejected_with_the_new_message()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", new { name = "New Route", areaId = 0 }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe("el sector es obligatorio");
    }

    // The §1 hole this plan closes: UpdateRoute never checked Name before, so a PUT with a
    // blank name blanked the route and its slug. This is the regression test that hole
    // never had.
    [Fact]
    public async Task PUT_with_a_blank_name_is_rejected()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        var areaId = await SeedAreaAsync();

        var createResponse = await moderator.Client.PostAsJsonAsync(
            "/api/routes", new { name = "New Route", areaId }, TestContext.Current.CancellationToken);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var putResponse = await moderator.Client.PutAsJsonAsync(
            $"/api/routes/{created!.Id}", new { name = "  ", areaId }, TestContext.Current.CancellationToken);

        putResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await putResponse.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe("el nombre es obligatorio");
    }

    // SEC-7: Name/Description were the only two write-path fields with no length check at
    // all (unlike ObligatoryGrade/RecommendedGear/OptionalGear, each already validated
    // before this) — an unbounded write up to Kestrel's body cap.
    [Fact]
    public async Task POST_with_a_name_over_200_characters_is_rejected()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", new { name = new string('a', 201), areaId }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe("el texto es demasiado largo");
    }

    [Fact]
    public async Task POST_with_a_description_over_5000_characters_is_rejected()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", new { name = "New Route", areaId, description = new string('a', 5001) },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe("el texto es demasiado largo");
    }

    [Fact]
    public async Task A_name_and_description_at_exactly_the_limit_are_accepted()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes",
            new { name = new string('a', 200), areaId, description = new string('b', 5000) },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
