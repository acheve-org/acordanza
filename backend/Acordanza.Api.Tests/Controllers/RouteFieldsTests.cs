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

// Issue #151: Length/ObligatoryGrade/RecommendedGear/OptionalGear/Links on ClimbingRoute —
// see plan 16. Everything here goes through real HTTP, so the DTO round trip is under
// test too, not just the entity.
public class RouteFieldsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record CreatedRouteResponse(int Id);

    private async Task<int> SeedAreaAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        return area.Id;
    }

    private static object RouteBody(
        int areaId, int? length = 100, string? obligatoryGrade = null, string? recommendedGear = null,
        string? optionalGear = null, List<string>? links = null) => new
    {
        name = "New Route",
        areaId,
        grade = "6a",
        orientation = (string?)null,
        gear = (string?)null,
        description = (string?)null,
        length,
        obligatoryGrade,
        recommendedGear,
        optionalGear,
        links,
        firstAscensionistIds = (List<int>?)null
    };

    [Theory]
    [InlineData(5)]
    [InlineData(3000)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Length_outside_the_valid_range_is_rejected(int length)
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, length), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(2999)]
    public async Task Length_at_the_valid_boundaries_is_accepted(int length)
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, length), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Omitting_length_from_the_request_body_is_rejected_not_silently_stored_as_zero()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync("/api/routes", new
        {
            name = "New Route",
            areaId,
            grade = "6a"
        }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Optional_strings_default_to_null_and_blank_input_normalizes_to_null()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var withoutThem = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId), TestContext.Current.CancellationToken);
        withoutThem.EnsureSuccessStatusCode();
        var createdWithoutThem = await withoutThem.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var detailWithoutThem = await user.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{createdWithoutThem!.Id}", TestContext.Current.CancellationToken);
        detailWithoutThem!.ObligatoryGrade.ShouldBeNull();
        detailWithoutThem.RecommendedGear.ShouldBeNull();
        detailWithoutThem.OptionalGear.ShouldBeNull();

        var withBlanks = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, obligatoryGrade: "  ", recommendedGear: "  ", optionalGear: "  "),
            TestContext.Current.CancellationToken);
        withBlanks.EnsureSuccessStatusCode();
        var createdWithBlanks = await withBlanks.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var detailWithBlanks = await user.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{createdWithBlanks!.Id}", TestContext.Current.CancellationToken);
        detailWithBlanks!.ObligatoryGrade.ShouldBeNull();
        detailWithBlanks.RecommendedGear.ShouldBeNull();
        detailWithBlanks.OptionalGear.ShouldBeNull();
    }

    [Fact]
    public async Task Overlong_obligatory_grade_is_rejected_with_a_400_not_a_500()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, obligatoryGrade: new string('a', 21)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Overlong_recommended_gear_is_rejected_with_a_400_not_a_500()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, recommendedGear: new string('a', 501)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_valid_https_link_set_round_trips_in_order()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();
        List<string> links = ["https://example.com/a", "http://example.com/b"];

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, links: links), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var detail = await user.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{created!.Id}", TestContext.Current.CancellationToken);
        detail!.Links.Select(l => l.Url).ShouldBe(links);
        detail.Links.Select(l => l.Source).ShouldAllBe(source => source == "human");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("ftp://x/y")]
    [InlineData("not a url")]
    public async Task A_link_with_a_disallowed_scheme_or_shape_is_rejected(string link)
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, links: [link]), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task More_than_ten_links_is_rejected()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();
        var links = Enumerable.Range(1, 11).Select(i => $"https://example.com/{i}").ToList();

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, links: links), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Duplicate_links_collapse_and_blanks_are_dropped()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var areaId = await SeedAreaAsync();
        List<string> links = ["https://example.com/a", "https://example.com/a", "  ", "https://example.com/b"];

        var response = await user.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, links: links), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var detail = await user.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{created!.Id}", TestContext.Current.CancellationToken);
        detail!.Links.Select(l => l.Url).ShouldBe(["https://example.com/a", "https://example.com/b"]);
    }

    [Fact]
    public async Task PUT_with_a_shorter_link_set_deletes_the_orphan_rows()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        var areaId = await SeedAreaAsync();

        var createResponse = await moderator.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, links: ["https://example.com/a", "https://example.com/b"]),
            TestContext.Current.CancellationToken);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var putResponse = await moderator.Client.PutAsJsonAsync(
            $"/api/routes/{created!.Id}",
            new UpsertRouteRequest("New Route", areaId, "6a", null, null, null, 100, null, null, null,
                ["https://example.com/a"], []),
            TestContext.Current.CancellationToken);
        putResponse.EnsureSuccessStatusCode();

        var detail = await moderator.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{created.Id}", TestContext.Current.CancellationToken);
        detail!.Links.Select(l => l.Url).ShouldBe(["https://example.com/a"]);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var remainingLinks = await db.Links.Where(l => l.RouteId == created.Id).CountAsync(TestContext.Current.CancellationToken);
        remainingLinks.ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_a_route_deletes_its_links()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        var areaId = await SeedAreaAsync();

        var createResponse = await moderator.Client.PostAsJsonAsync(
            "/api/routes", RouteBody(areaId, links: ["https://example.com/a"]), TestContext.Current.CancellationToken);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedRouteResponse>(TestContext.Current.CancellationToken);

        var deleteResponse = await moderator.Client.DeleteAsync($"/api/routes/{created!.Id}", TestContext.Current.CancellationToken);
        deleteResponse.EnsureSuccessStatusCode();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var remainingLinks = await db.Links.Where(l => l.RouteId == created.Id).CountAsync(TestContext.Current.CancellationToken);
        remainingLinks.ShouldBe(0);
    }
}
