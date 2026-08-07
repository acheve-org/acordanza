using System.Net;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Infrastructure;

public class ApiFixtureSmokeTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Unauthenticated_request_to_a_protected_endpoint_returns_401()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync("/api/routes", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
