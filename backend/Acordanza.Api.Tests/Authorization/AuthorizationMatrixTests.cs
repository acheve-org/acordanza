using System.Net;
using System.Text;
using Acordanza.Api.Authorization;
using Acordanza.Api.Tests.Infrastructure;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Authorization;

// Every write endpoint gated by [Authorize(Policy = Permissions.X)], crossed with every
// role. This is the app's core security property (backend/CLAUDE.md: "write access is
// role-based") and the thing most likely to break silently when a new endpoint forgets
// its policy attribute — see plan 08's slice 1. Pair with PermissionsTests (plan 07),
// which catches the same class of bug from the Permissions.All side.
//
// Endpoints are hit with a fake/non-existent id (999999) where one is needed: since
// ASP.NET Core's authorization middleware runs before model binding and before the
// action body, an insufficiently-permissioned role gets 403 regardless of whether the
// target exists — so no seed data is needed to prove the boundary. A permitted role may
// instead see 404/400/409 once past the gate (e.g. "area doesn't exist"); the assertion
// only cares that it isn't 401/403.
public class AuthorizationMatrixTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    public record EndpointCase(string Name, HttpMethod Method, string Path, string RequiredPermission, Func<HttpContent?>? BuildBody = null)
    {
        public override string ToString() => Name;
    }

    private static HttpContent Json(string json) => new StringContent(json, Encoding.UTF8, "application/json");

    public static TheoryData<EndpointCase> Endpoints => new()
    {
        new EndpointCase("Create route", HttpMethod.Post, "/api/routes", Permissions.RouteCreate,
            () => Json("""{"name":"x","areaId":0,"grade":"6a"}""")),
        new EndpointCase("Edit route", HttpMethod.Put, "/api/routes/999999", Permissions.RouteEdit,
            () => Json("""{"name":"x","areaId":0,"grade":"6a"}""")),
        new EndpointCase("Delete route", HttpMethod.Delete, "/api/routes/999999", Permissions.RouteDelete),
        new EndpointCase("Tick a route", HttpMethod.Post, "/api/routes/999999/ticks", Permissions.TickCreate,
            () => Json("""{"date":"2024-01-01"}""")),

        new EndpointCase("Create area", HttpMethod.Post, "/api/areas", Permissions.AreaCreate,
            () => Json($$"""{"name":"area-{{Guid.NewGuid():N}}"}""")),
        new EndpointCase("Edit area", HttpMethod.Put, "/api/areas/999999", Permissions.AreaEdit,
            () => Json("""{"name":"x"}""")),
        new EndpointCase("Delete area", HttpMethod.Delete, "/api/areas/999999", Permissions.AreaDelete),
        new EndpointCase("Edit area coordinates", HttpMethod.Put, "/api/areas/999999/coordinates", Permissions.AreaEdit,
            () => Json("""{"polygon":[]}""")),
        new EndpointCase("Validate area", HttpMethod.Post, "/api/areas/999999/validate", Permissions.AreaValidate),
        new EndpointCase("List pending areas", HttpMethod.Get, "/api/areas/pending", Permissions.AreaValidate),

        new EndpointCase("List pending area experts", HttpMethod.Get, "/api/area-experts/pending", Permissions.AreaExpertApprove),
        new EndpointCase("Request area expert", HttpMethod.Post, "/api/area-experts", Permissions.AreaExpertRequest,
            () => Json("""{"areaId":999999}""")),
        new EndpointCase("Approve area expert", HttpMethod.Post, "/api/area-experts/999999/approve", Permissions.AreaExpertApprove),
        new EndpointCase("Reject area expert", HttpMethod.Post, "/api/area-experts/999999/reject", Permissions.AreaExpertApprove,
            () => Json("""{"reason":"x"}""")),

        new EndpointCase("Create first ascensionist", HttpMethod.Post, "/api/first-ascensionists", Permissions.FirstAscensionistCreate,
            () => Json($$"""{"name":"fa-{{Guid.NewGuid():N}}"}""")),
        new EndpointCase("Edit first ascensionist", HttpMethod.Put, "/api/first-ascensionists/999999", Permissions.FirstAscensionistEdit,
            () => Json("""{"name":"x"}""")),
        new EndpointCase("Delete first ascensionist", HttpMethod.Delete, "/api/first-ascensionists/999999", Permissions.FirstAscensionistDelete),

        new EndpointCase("List pending first-ascensionist links", HttpMethod.Get, "/api/first-ascensionist-links/pending", Permissions.FirstAscensionistLinkApprove),
        new EndpointCase("Request first-ascensionist link", HttpMethod.Post, "/api/first-ascensionist-links", Permissions.FirstAscensionistLinkRequest,
            () => Json("""{"firstAscensionistId":999999}""")),
        new EndpointCase("Approve first-ascensionist link", HttpMethod.Post, "/api/first-ascensionist-links/999999/approve", Permissions.FirstAscensionistLinkApprove),
        new EndpointCase("Reject first-ascensionist link", HttpMethod.Post, "/api/first-ascensionist-links/999999/reject", Permissions.FirstAscensionistLinkApprove,
            () => Json("""{"reason":"x"}""")),

        new EndpointCase("Delete tick", HttpMethod.Delete, "/api/ticks/999999", Permissions.TickDelete),

        new EndpointCase("List all users", HttpMethod.Get, "/api/users", Permissions.UserManage),
        new EndpointCase("List my invites", HttpMethod.Get, "/api/users/my-invites", Permissions.UserInvite),
        new EndpointCase("Invite a user", HttpMethod.Post, "/api/users/invite", Permissions.UserInvite,
            () => Json($$"""{"email":"{{Guid.NewGuid():N}}@example.test","role":"user"}"""))
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Every_role_gets_the_expected_outcome(EndpointCase endpoint)
    {
        foreach (var role in Roles.All)
        {
            using var user = await TestUsers.CreateAsync(Factory, role);
            var isAllowed = RolePermissions.Map[role].Contains(endpoint.RequiredPermission);

            using var request = new HttpRequestMessage(endpoint.Method, endpoint.Path);
            if (endpoint.BuildBody is not null)
            {
                request.Content = endpoint.BuildBody();
            }

            var response = await user.Client.SendAsync(request, TestContext.Current.CancellationToken);

            if (isAllowed)
            {
                response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden,
                    $"role '{role}' has {endpoint.RequiredPermission} and should not be forbidden from {endpoint.Name}");
                response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized,
                    $"role '{role}' should be authenticated for {endpoint.Name}");
            }
            else
            {
                response.StatusCode.ShouldBe(HttpStatusCode.Forbidden,
                    $"role '{role}' lacks {endpoint.RequiredPermission} and should be forbidden from {endpoint.Name}, got {response.StatusCode}");
            }
        }
    }
}
