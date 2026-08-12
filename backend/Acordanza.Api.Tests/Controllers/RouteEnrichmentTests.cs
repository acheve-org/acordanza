using System.Net;
using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Acordanza.Api.Tests.Infrastructure;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// Plan 18 (route-enrichment-worker), issue #169. Only the HTTP trigger
// (POST /routes/{id}/enrich) lives here — RoutesController is an Api concern, so it's
// exercised through real requests against the shared fixture. The write paths (link
// filtering, topo verify/import, idempotency via RouteEnrichmentService/
// EnrichmentQueueProcessor) moved to Acordanza.Worker.Tests (plan 27,
// worker-test-split) — those are worker concerns and no longer need the Api host at
// all, only Acordanza.Worker's own DI graph.
public class RouteEnrichmentTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task<(Area Area, ClimbingRoute Route)> SeedRouteAsync(string addedByUserId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, addedByUserId);
        return (area, route);
    }

    private QueueClient QueueClient => Factory.Services.GetRequiredService<QueueClient>();

    private async Task<int> QueueLengthAsync()
    {
        var props = await QueueClient.GetPropertiesAsync(TestContext.Current.CancellationToken);
        return props.Value.ApproximateMessagesCount;
    }

    // --- HTTP trigger ---

    [Fact]
    public async Task Enrich_as_admin_returns_202_and_enqueues_the_route_id()
    {
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);
        var (_, route) = await SeedRouteAsync(admin.UserId);

        var response = await admin.Client.PostAsync($"/api/routes/{route.Id}/enrich", null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await QueueLengthAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Enrich_an_unknown_route_returns_404()
    {
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);

        var response = await admin.Client.PostAsync("/api/routes/999999/enrich", null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Creating_a_route_leaves_the_queue_empty()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        await SeedRouteAsync(user.UserId);

        // No enqueue at all from CreateRoute — the manual-trigger decision (plan 18):
        // this is the regression the create-time hook's removal guards against.
        (await QueueLengthAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(Roles.Moderator)]
    [InlineData(Roles.User)]
    public async Task Enrich_is_forbidden_for_roles_without_the_permission(string role)
    {
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);
        var (_, route) = await SeedRouteAsync(admin.UserId);
        using var caller = await TestUsers.CreateAsync(Factory, role);

        var response = await caller.Client.PostAsync($"/api/routes/{route.Id}/enrich", null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await QueueLengthAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Enrich_is_forbidden_for_a_first_ascensionist_linked_user()
    {
        // This is the case worth an explicit test: route.edit is granted dynamically to
        // a linked first ascensionist, so reusing route.edit for the enrich policy
        // (rejected in the plan) would have let exactly this caller through.
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);
        var (_, route) = await SeedRouteAsync(admin.UserId);
        using var linked = await TestUsers.CreateFirstAscensionistLinkedAsync(Factory, "Linked Opener");

        var response = await linked.Client.PostAsync($"/api/routes/{route.Id}/enrich", null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await QueueLengthAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Retriggering_an_already_enriched_route_returns_409_unless_forced()
    {
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);
        var (_, route) = await SeedRouteAsync(admin.UserId);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracked = await db.Routes.FindAsync([route.Id], TestContext.Current.CancellationToken);
            tracked!.EnrichedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var conflict = await admin.Client.PostAsync($"/api/routes/{route.Id}/enrich", null, TestContext.Current.CancellationToken);
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await QueueLengthAsync()).ShouldBe(0);

        var forced = await admin.Client.PostAsync($"/api/routes/{route.Id}/enrich?force=true", null, TestContext.Current.CancellationToken);
        forced.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await QueueLengthAsync()).ShouldBe(1);
    }
}
