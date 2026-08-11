using System.Net;
using System.Net.Http;
using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Models;
using Acordanza.Api.Services;
using Acordanza.Api.Tests.Infrastructure;
using Azure.Storage.Queues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// Plan 18 (route-enrichment-worker), issue #169. HTTP-trigger behaviour goes through
// real requests against the shared fixture; the write paths (link filtering, topo
// verify/import, idempotency) are exercised by constructing RouteEnrichmentService
// directly with a fake provider, per the plan's testing note — no real Claude call is
// ever made in CI.
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
            var tracked = await db.Routes.FindAsync(route.Id);
            tracked!.EnrichedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var conflict = await admin.Client.PostAsync($"/api/routes/{route.Id}/enrich", null, TestContext.Current.CancellationToken);
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await QueueLengthAsync()).ShouldBe(0);

        var forced = await admin.Client.PostAsync($"/api/routes/{route.Id}/enrich?force=true", null, TestContext.Current.CancellationToken);
        forced.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await QueueLengthAsync()).ShouldBe(1);
    }

    // --- Processing (RouteEnrichmentService, driven directly with a fake provider) ---

    private sealed class FakeProvider(List<string> links, string? topoImageUrl) : IRouteEnrichmentProvider
    {
        public int CallCount { get; private set; }

        public Task<EnrichmentResult> FindAsync(RouteEnrichmentContext context, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new EnrichmentResult(links, topoImageUrl));
        }
    }

    private sealed class FakeTopoVerification(bool isTopo) : ITopoVerificationProvider
    {
        public Task<bool> IsTopoAsync(TopoVerificationContext context, CancellationToken cancellationToken) =>
            Task.FromResult(isTopo);
    }

    private sealed class StubHttpMessageHandler(HttpStatusCode status, byte[]? body, string? contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status);
            if (body is not null)
            {
                response.Content = new ByteArrayContent(body);
                if (contentType is not null)
                {
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                }
            }
            return Task.FromResult(response);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private RouteEnrichmentService BuildService(
        AppDbContext db, IRouteEnrichmentProvider provider, ITopoVerificationProvider? topoVerification = null,
        HttpMessageHandler? httpHandler = null)
    {
        var areaHierarchy = new AreaHierarchyService(db);
        var mediaUpload = Factory.Services.GetRequiredService<MediaUploadService>();
        var blobStorage = Factory.Services.GetRequiredService<BlobStorageService>();
        var httpClientFactory = new StubHttpClientFactory(httpHandler ?? new StubHttpMessageHandler(HttpStatusCode.NotFound, null, null));

        return new RouteEnrichmentService(
            db, areaHierarchy, provider, topoVerification ?? new FakeTopoVerification(false),
            mediaUpload, blobStorage, httpClientFactory, NullLogger<RouteEnrichmentService>.Instance);
    }

    [Fact]
    public async Task Found_links_are_filtered_deduped_and_appended_within_the_cap()
    {
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);
        var (_, route) = await SeedRouteAsync(admin.UserId);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tracked = await db.Routes.Include(r => r.Links).FirstAsync(r => r.Id == route.Id);
        tracked.Links.Add(new Link { Url = "https://example.com/existing" });
        await db.SaveChangesAsync();

        var found = new List<string>
        {
            "https://example.com/existing", // duplicate — dropped
            "javascript:alert(1)",          // scheme not allowed — dropped by LinkRules
            "https://example.com/new"
        };
        var provider = new FakeProvider(found, null);
        var service = BuildService(db, provider);

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);

        var reloaded = await db.Routes.Include(r => r.Links).FirstAsync(r => r.Id == route.Id);
        reloaded.Links.Select(l => l.Url).ShouldBe(["https://example.com/existing", "https://example.com/new"]);
        reloaded.EnrichedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Reprocessing_an_already_enriched_route_skips_the_provider_call_unless_forced()
    {
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);
        var (_, route) = await SeedRouteAsync(admin.UserId);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var provider = new FakeProvider([], null);
        var service = BuildService(db, provider);

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);
        provider.CallCount.ShouldBe(1);

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);
        provider.CallCount.ShouldBe(1); // idempotent: no second call

        await service.EnrichAsync(route.Id, force: true, TestContext.Current.CancellationToken);
        provider.CallCount.ShouldBe(2); // force re-runs
    }

    [Fact]
    public async Task An_approved_route_stays_approved_after_enrichment()
    {
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);
        var (_, route) = await SeedRouteAsync(admin.UserId); // SeedData.CreateRouteAsync validates by default

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var provider = new FakeProvider(["https://example.com/found"], null);
        var service = BuildService(db, provider);

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);

        var reloaded = await db.Routes.FindAsync(route.Id);
        reloaded!.ValidationStatus.ShouldBe(ReviewStatus.Approved);
    }

    [Fact]
    public async Task A_verified_topo_candidate_is_imported()
    {
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);
        var (_, route) = await SeedRouteAsync(admin.UserId);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var provider = new FakeProvider([], "https://example.com/topo.jpg");
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, [1, 2, 3, 4], "image/jpeg");
        var service = BuildService(db, provider, new FakeTopoVerification(true), handler);

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);

        var topos = await db.Topos.Where(t => t.RouteId == route.Id).ToListAsync(TestContext.Current.CancellationToken);
        topos.Count.ShouldBe(1);
        topos[0].UploadedByUserId.ShouldBe(admin.UserId);
    }

    [Fact]
    public async Task An_unverified_topo_candidate_is_not_imported()
    {
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);
        var (_, route) = await SeedRouteAsync(admin.UserId);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var provider = new FakeProvider([], "https://example.com/topo.jpg");
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, [1, 2, 3, 4], "image/jpeg");
        var service = BuildService(db, provider, new FakeTopoVerification(false), handler);

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);

        var topos = await db.Topos.Where(t => t.RouteId == route.Id).ToListAsync(TestContext.Current.CancellationToken);
        topos.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_route_with_an_existing_topo_skips_the_topo_step()
    {
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, admin.UserId, hasTopo: true);

        var provider = new FakeProvider([], "https://example.com/topo.jpg");
        // Verification would return true, but it must never be called — hasTopo
        // already makes this step idempotent regardless of EnrichedAt.
        var service = BuildService(db, provider, new FakeTopoVerification(true));

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);

        var topos = await db.Topos.Where(t => t.RouteId == route.Id).ToListAsync(TestContext.Current.CancellationToken);
        topos.Count.ShouldBe(1); // still just the pre-existing one
    }

    [Fact]
    public async Task An_oversized_or_wrong_content_type_download_skips_the_topo_cleanly()
    {
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin);
        var (_, route) = await SeedRouteAsync(admin.UserId);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var provider = new FakeProvider([], "https://example.com/topo.svg");
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, [1, 2, 3], "image/svg+xml");
        var service = BuildService(db, provider, new FakeTopoVerification(true), handler);

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);

        var topos = await db.Topos.Where(t => t.RouteId == route.Id).ToListAsync(TestContext.Current.CancellationToken);
        topos.ShouldBeEmpty();
        var reloaded = await db.Routes.FindAsync(route.Id);
        reloaded!.EnrichedAt.ShouldNotBeNull(); // links/EnrichedAt still land even though the topo didn't
    }

    // --- Poison handling (EnrichmentQueueProcessor) ---

    [Fact]
    public async Task An_unparseable_message_is_dropped_without_retrying()
    {
        await QueueClient.SendMessageAsync("not json", TestContext.Current.CancellationToken);

        using var scope = Factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<EnrichmentQueueProcessor>();
        await processor.RunAsync(TestContext.Current.CancellationToken);

        (await QueueLengthAsync()).ShouldBe(0);
    }
}
