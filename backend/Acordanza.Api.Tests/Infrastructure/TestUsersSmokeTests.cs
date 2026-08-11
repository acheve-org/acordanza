using System.Net;
using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Infrastructure;

public class TestUsersSmokeTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateAsync_logs_in_and_returns_a_bearer_authenticated_client()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);

        var response = await user.Client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SeedData_creates_an_area_and_route_queryable_through_the_api()
    {
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var area = await SeedData.CreateAreaAsync(db, "Smoke Test Area");
            await SeedData.CreateRouteAsync(db, area, admin.UserId, name: "Smoke Test Route");
        }

        var response = await admin.Client.GetAsync("/api/routes", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("Smoke Test Route");
    }
}
