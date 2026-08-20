using System.Net;
using System.Text;
using Acordanza.Api.Authorization;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;
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

    // The one multipart endpoint in the matrix (POST /route-imports). Authorization runs
    // before model binding, so the parts only have to be well-formed enough to reach the
    // action at all — an unauthorized role never gets that far either way.
    private static HttpContent Multipart()
    {
        var file = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xD9]);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent
        {
            { file, "file", "topo.jpg" },
            { new StringContent("999999"), "areaId" }
        };
    }

    private static readonly EndpointCase[] Cases =
    [
        new EndpointCase("Create route", HttpMethod.Post, "/api/routes", Permissions.RouteCreate,
            () => Json("""{"name":"x","areaId":0,"grade":"6a"}""")),
        new EndpointCase("Edit route", HttpMethod.Put, "/api/routes/999999", Permissions.RouteEdit,
            () => Json("""{"name":"x","areaId":0,"grade":"6a"}""")),
        new EndpointCase("Delete route", HttpMethod.Delete, "/api/routes/999999", Permissions.RouteDelete),
        new EndpointCase("Tick a route", HttpMethod.Post, "/api/routes/999999/ticks", Permissions.TickCreate,
            () => Json("""{"date":"2024-01-01"}""")),
        // The only admin-only endpoint in the app that exists because it *spends money* rather
        // than because it changes data — route.enrich is deliberately narrower than route.edit
        // (see Permissions.RouteEnrich), and this row is what says so out loud.
        new EndpointCase("Enrich a route", HttpMethod.Post, "/api/routes/999999/enrich", Permissions.RouteEnrich),
        // Both import kinds are gated on route.create, deliberately not a new permission and
        // not route.enrich — see RouteImportsController. A permitted role falls through to a
        // 400 here ("el sector no existe"), which the assertion below is fine with.
        new EndpointCase("Import a route from a topo", HttpMethod.Post, "/api/route-imports/image", Permissions.RouteCreate,
            Multipart),
        new EndpointCase("Import a route from a url", HttpMethod.Post, "/api/route-imports/url", Permissions.RouteCreate,
            () => Json("""{"url":"https://example.com/via","areaId":999999}""")),

        new EndpointCase("Create area", HttpMethod.Post, "/api/areas", Permissions.AreaCreate,
            () => Json($$"""{"name":"area-{{Guid.NewGuid():N}}"}""")),
        new EndpointCase("Edit area", HttpMethod.Put, "/api/areas/999999", Permissions.AreaEdit,
            () => Json("""{"name":"x"}""")),
        new EndpointCase("Delete area", HttpMethod.Delete, "/api/areas/999999", Permissions.AreaDelete),
        new EndpointCase("Edit area coordinates", HttpMethod.Put, "/api/areas/999999/coordinates", Permissions.AreaEdit,
            () => Json("""{"polygon":[]}""")),
        new EndpointCase("Validate area", HttpMethod.Post, "/api/areas/999999/validate", Permissions.AreaValidate),
        new EndpointCase("Reject area", HttpMethod.Post, "/api/areas/999999/reject", Permissions.AreaValidate,
            () => Json("""{"reason":"x"}""")),
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
        new EndpointCase("Validate first ascensionist", HttpMethod.Post, "/api/first-ascensionists/999999/validate", Permissions.FirstAscensionistValidate),
        new EndpointCase("Reject first ascensionist", HttpMethod.Post, "/api/first-ascensionists/999999/reject", Permissions.FirstAscensionistValidate,
            () => Json("""{"reason":"x"}""")),
        new EndpointCase("List pending first ascensionists", HttpMethod.Get, "/api/first-ascensionists/pending", Permissions.FirstAscensionistValidate),
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
    ];

    public static TheoryData<EndpointCase> Endpoints => new(Cases);

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

    // ---- Keeping the matrix honest -------------------------------------------------
    //
    // The list above is maintained by hand, and a list like that decays in two directions.
    //
    // A row can outlive the endpoint it names: "Import a route from a topo" pointed at
    // POST /api/route-imports long after the controller split that into /image and /url.
    // Nothing caught it, because every role holds route.create, so the case could only ever
    // assert "not 401/403" — and a 404 from an unrouted path satisfies that perfectly well.
    // The row passed for years while testing nothing, and both real endpoints went uncovered.
    //
    // And a new write endpoint can ship with no row at all, which is how
    // DELETE /api/topos/{id} spent its life with no authorization check on it.
    //
    // The two tests below close both directions against the endpoints the app actually
    // registers, so neither failure mode can be silent again.

    private sealed record RegisteredEndpoint(string Method, string Template)
    {
        public override string ToString() => $"{Method} {Template}";
    }

    // Write endpoints deliberately not gated by an [Authorize(Policy = ...)] attribute, so
    // the matrix has nothing to assert about them. Every entry names why it's exempt and
    // what covers it instead — an entry here is a claim someone checked, not a way to opt
    // out of thinking about it.
    private static readonly Dictionary<string, string> WriteEndpointsWithoutAPolicy = new()
    {
        // Anonymous by design: reached while logged out, or from whatever browser opened an
        // emailed link. Covered by AuthFlowsTests / EmailChangeTests.
        ["POST api/auth/login"] = "anonymous — the login itself",
        ["POST api/auth/login/passkey"] = "anonymous — second step of login",
        ["POST api/auth/login/passkey/options"] = "anonymous — rate-limited, see Program.cs",
        ["POST api/auth/forgot-password"] = "anonymous — rate-limited, no account enumeration",
        ["POST api/auth/reset-password"] = "anonymous — the emailed link's own token is the credential",
        ["POST api/auth/validate-token"] = "anonymous — read-only pre-check of the two link tokens",
        ["POST api/auth/accept-invite"] = "anonymous — single-use invite token is the credential",
        ["POST api/auth/confirm-email-change"] = "anonymous — token embeds the new address and the security stamp",

        // Self-service: [Authorize] plus a scope to the caller's own row/account, which is a
        // stronger check than any role could express. Covered by EmailChangeTests,
        // AuthFlowsTests and the per-controller tests named alongside.
        ["POST api/account/change-password"] = "self-service — re-authenticates with the current password",
        ["PUT api/account/profile"] = "self-service — the caller's own name",
        ["POST api/account/email/change-request"] = "self-service — re-authenticates, rate-limited",
        ["POST api/passkeys/register/options"] = "self-service — the caller's own credential",
        ["POST api/passkeys/register/complete"] = "self-service — challenge is bound to the caller's id",
        ["DELETE api/passkeys/{id:int}"] = "self-service — scoped to the caller's own passkey",
        ["DELETE api/area-experts/{id:int}"] = "self-service — cancels the caller's own request",
        ["DELETE api/first-ascensionist-links/{id:int}"] = "self-service — cancels the caller's own request",
        ["DELETE api/route-imports/{id:int}"] = "self-service — scoped to the caller's own import",

        // Ownership scoped to a query rather than a claim (AreaExpertAuthorizer): the caller
        // may act on some areas and not others, which an [Authorize] attribute cannot say.
        // These return 404 before the check when the target doesn't exist, so the matrix's
        // fake-id trick wouldn't work on them anyway — they need seeded data, which is why
        // they live in the tests named here instead.
        ["POST api/routes/{id:int}/validate"] = "area-scoped — RouteValidationLifecycleTests",
        ["POST api/routes/{id:int}/reject"] = "area-scoped — RouteValidationLifecycleTests",
        ["POST api/areas/{id:int}/media"] = "area-scoped — AreaExpertAuthorizer, see AreasTests",
        ["DELETE api/area-media/{id:int}"] = "area-scoped — AreaExpertAuthorizer, see AreasTests",
        ["DELETE api/topos/{id:int}"] = "uploader-or-area-scoped — TopoDeletionTests",

        // Deliberately open to every account: adding a photo to a route is additive, and the
        // UI offers it to everyone. Pinned by TopoDeletionTests so the openness stays a
        // decision rather than becoming one by neglect.
        ["POST api/routes/{id:int}/topos"] = "open to any account by design — TopoDeletionTests"
    };

    // Every (method, template) pair MVC actually routes, straight off the action descriptors
    // rather than by reflecting over attributes — what the app registers is the thing these
    // tests need to be true about, and a [Route] on the controller means the two differ.
    private IReadOnlyList<RegisteredEndpoint> RegisteredEndpoints() =>
        Factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .Where(action => action.AttributeRouteInfo?.Template is not null)
            .SelectMany(action => action.ActionConstraints?
                .OfType<HttpMethodActionConstraint>()
                .SelectMany(constraint => constraint.HttpMethods)
                .Select(method => new RegisteredEndpoint(method, action.AttributeRouteInfo!.Template!)) ?? [])
            .Distinct()
            .OrderBy(endpoint => endpoint.ToString())
            .ToList();

    private IEnumerable<RegisteredEndpoint> RegisteredWriteEndpoints() =>
        RegisteredEndpoints().Where(endpoint => endpoint.Method is "POST" or "PUT" or "DELETE");

    // Template-vs-concrete-path matching, so the matrix keeps naming the URL a client would
    // actually call ("/api/routes/999999") rather than a route template nobody types.
    private static bool Matches(RegisteredEndpoint endpoint, EndpointCase row) =>
        string.Equals(endpoint.Method, row.Method.Method, StringComparison.OrdinalIgnoreCase) &&
        new TemplateMatcher(TemplateParser.Parse(endpoint.Template), new RouteValueDictionary())
            .TryMatch(row.Path, new RouteValueDictionary());

    [Fact]
    public void Every_matrix_row_points_at_an_endpoint_that_still_exists()
    {
        // Every method, not just the write ones: the matrix also carries the pending-review
        // queues, which are GETs gated by the same policies and worth the same assertion.
        var registered = RegisteredEndpoints();

        var dead = Cases
            .Where(row => !registered.Any(endpoint => Matches(endpoint, row)))
            .Select(row => $"{row.Name} ({row.Method.Method} {row.Path})")
            .ToList();

        dead.ShouldBeEmpty(
            "these rows name a path the app no longer routes, so they assert nothing — " +
            $"repoint or remove them: {string.Join(", ", dead)}");
    }

    [Fact]
    public void Every_write_endpoint_is_either_in_the_matrix_or_explicitly_exempt()
    {
        var uncovered = RegisteredWriteEndpoints()
            .Where(endpoint => !WriteEndpointsWithoutAPolicy.ContainsKey(endpoint.ToString()))
            .Where(endpoint => !Cases.Any(row => Matches(endpoint, row)))
            .Select(endpoint => endpoint.ToString())
            .ToList();

        uncovered.ShouldBeEmpty(
            "every write endpoint needs a row in the matrix above, or an entry in " +
            $"{nameof(WriteEndpointsWithoutAPolicy)} saying why it has no policy and what covers it " +
            $"instead: {string.Join(", ", uncovered)}");
    }
}
