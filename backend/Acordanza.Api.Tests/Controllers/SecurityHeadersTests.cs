using System.Net;
using Acordanza.Api.Authorization;
using Acordanza.Api.Tests.Infrastructure;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// The API used to return no security headers at all. These are set by a middleware in
// Program.cs rather than per-controller, so the thing worth asserting is that they're on
// *every* response — including the ones that never reach an action, which is where a
// pipeline-ordering mistake would show up first.
//
// The app's own headers are nginx's (see frontend/nginx.conf) and the landing page's are
// Static Web Apps' (landing/staticwebapp.config.json); neither is reachable from here.
public class SecurityHeadersTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly (string Name, string Value)[] Expected =
    [
        ("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'; base-uri 'none'"),
        ("X-Content-Type-Options", "nosniff"),
        ("X-Frame-Options", "DENY"),
        ("Referrer-Policy", "no-referrer")
    ];

    private static void ShouldCarryTheHeaders(HttpResponseMessage response, string what)
    {
        foreach (var (name, value) in Expected)
        {
            response.Headers.TryGetValues(name, out var values).ShouldBeTrue($"{what} should carry {name}");
            values!.ShouldBe([value], $"{what} should carry the expected {name}");
        }
    }

    [Fact]
    public async Task A_successful_response_carries_the_security_headers()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);

        var response = await user.Client.GetAsync("/api/routes", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldCarryTheHeaders(response, "a 200");
    }

    // The case the middleware's position in the pipeline decides: a request rejected by the
    // authentication middleware never reaches an action, so headers added any later than
    // this wouldn't be on it.
    [Fact]
    public async Task An_unauthenticated_response_carries_the_security_headers()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/api/routes", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ShouldCarryTheHeaders(response, "a 401");
    }

    [Fact]
    public async Task A_forbidden_response_carries_the_security_headers()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);

        // user holds neither user.manage nor route.enrich — see RolePermissions.
        var response = await user.Client.GetAsync("/api/users", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ShouldCarryTheHeaders(response, "a 403");
    }

    // Not a routed path at all, so nothing but the middleware can have touched it.
    [Fact]
    public async Task A_not_found_response_carries_the_security_headers()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/api/nothing-here", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ShouldCarryTheHeaders(response, "a 404");
    }
}
