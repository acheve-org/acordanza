using System.Net;
using System.Net.Http.Json;
using Acordanza.Api.Authorization;
using Acordanza.Api.Dtos;
using Acordanza.Api.Tests.Infrastructure;
using Acordanza.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// DELETE /api/topos/{id} used to carry nothing but the class-level [Authorize], so any
// account could delete any route photo by id — ids are sequential and the blob goes
// immediately. The rule now is the one in ToposController: the uploader, or anyone who can
// edit the route (role, first-ascensionist link, or an approved expert of the area).
//
// Uploading is deliberately *not* gated the same way, and there is a test below pinning
// that too — adding a photo is additive, and that asymmetry is a decision rather than the
// leftover this file exists to fix.
public class TopoDeletionTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task<(int RouteId, int TopoId, string AreaSlug, int AreaId)> SeedRouteWithTopoAsync(string uploaderUserId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, uploaderUserId, hasTopo: true);
        var topo = await db.Topos.AsNoTracking().FirstAsync(t => t.RouteId == route.Id);

        return (route.Id, topo.Id, area.Slug, area.Id);
    }

    private async Task<bool> TopoExistsAsync(int topoId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Topos.AnyAsync(t => t.Id == topoId);
    }

    [Fact]
    public async Task A_stranger_cannot_delete_someone_elses_topo()
    {
        using var uploader = await TestUsers.CreateAsync(Factory, Roles.User);
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        var (_, topoId, _, _) = await SeedRouteWithTopoAsync(uploader.UserId);

        var response = await stranger.Client.DeleteAsync($"/api/topos/{topoId}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TopoExistsAsync(topoId)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_uploader_can_delete_their_own_topo()
    {
        using var uploader = await TestUsers.CreateAsync(Factory, Roles.User);
        var (_, topoId, _, _) = await SeedRouteWithTopoAsync(uploader.UserId);

        var response = await uploader.Client.DeleteAsync($"/api/topos/{topoId}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TopoExistsAsync(topoId)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_moderator_can_delete_anyones_topo()
    {
        using var uploader = await TestUsers.CreateAsync(Factory, Roles.User);
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        var (_, topoId, _, _) = await SeedRouteWithTopoAsync(uploader.UserId);

        var response = await moderator.Client.DeleteAsync($"/api/topos/{topoId}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TopoExistsAsync(topoId)).ShouldBeFalse();
    }

    // The branch that can't be a JWT claim (see AreaExpertAuthorizer): a plain user with no
    // route.edit permission, who may still act inside the area they were approved for.
    [Fact]
    public async Task An_approved_area_expert_can_delete_a_topo_in_their_area()
    {
        using var uploader = await TestUsers.CreateAsync(Factory, Roles.User);
        var (_, topoId, _, areaId) = await SeedRouteWithTopoAsync(uploader.UserId);
        using var expert = await TestUsers.CreateAreaExpertAsync(Factory, areaId);

        var response = await expert.Client.DeleteAsync($"/api/topos/{topoId}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TopoExistsAsync(topoId)).ShouldBeFalse();
    }

    // The flag the frontend hides the delete button behind, so the UI can't offer an action
    // the API is going to refuse — and, more importantly, so it doesn't *stop* offering one
    // the API would allow.
    [Fact]
    public async Task Route_detail_reports_per_topo_whether_the_caller_may_delete_it()
    {
        using var uploader = await TestUsers.CreateAsync(Factory, Roles.User);
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        var (routeId, _, _, _) = await SeedRouteWithTopoAsync(uploader.UserId);

        var forUploader = await uploader.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{routeId}", TestContext.Current.CancellationToken);
        var forStranger = await stranger.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{routeId}", TestContext.Current.CancellationToken);
        var forModerator = await moderator.Client.GetFromJsonAsync<RouteDetailDto>(
            $"/api/routes/{routeId}", TestContext.Current.CancellationToken);

        forUploader!.Topos.Single().CanDelete.ShouldBeTrue();
        forStranger!.Topos.Single().CanDelete.ShouldBeFalse();
        forModerator!.Topos.Single().CanDelete.ShouldBeTrue();
    }

    // Deliberate, not an oversight: any account may add a photo to any route. Pinned so that
    // tightening deletion above doesn't get read later as "uploads must have been gated too".
    [Fact]
    public async Task Uploading_a_topo_stays_open_to_any_account()
    {
        using var uploader = await TestUsers.CreateAsync(Factory, Roles.User);
        using var other = await TestUsers.CreateAsync(Factory, Roles.User);
        var (routeId, _, _, _) = await SeedRouteWithTopoAsync(uploader.UserId);

        var file = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xD9]);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        using var content = new MultipartFormDataContent { { file, "file", "topo.jpg" } };

        var response = await other.Client.PostAsync(
            $"/api/routes/{routeId}/topos", content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = await response.Content.ReadFromJsonAsync<TopoDto>(TestContext.Current.CancellationToken);
        // ...and whoever uploaded it can take it back down again.
        created!.CanDelete.ShouldBeTrue();
    }
}
