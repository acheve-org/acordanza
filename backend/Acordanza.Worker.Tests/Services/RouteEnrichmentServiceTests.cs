using System.Net;
using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Acordanza.Core.Services;
using Acordanza.Worker.Services;
using Acordanza.Worker.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// Plan 18 (route-enrichment-worker), issue #169 — moved out of
// Acordanza.Api.Tests.Controllers.RouteEnrichmentTests (plan 27, worker-test-split):
// the HTTP-trigger behaviour stays there (it's RoutesController, an Api concern), but
// these write paths (link filtering, topo verify/import, idempotency) construct
// RouteEnrichmentService directly with a fake provider against this project's own
// Postgres+Azurite fixture — no real Claude call is ever made in CI.
public class RouteEnrichmentServiceTests(WorkerFixture fixture) : WorkerTestBase(fixture)
{
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
        var mediaUpload = Fixture.Services.GetRequiredService<MediaUploadService>();
        var blobStorage = Fixture.Services.GetRequiredService<BlobStorageService>();
        var httpClientFactory = new StubHttpClientFactory(httpHandler ?? new StubHttpMessageHandler(HttpStatusCode.NotFound, null, null));

        return new RouteEnrichmentService(
            db, areaHierarchy, provider, topoVerification ?? new FakeTopoVerification(false),
            mediaUpload, blobStorage, httpClientFactory, NullLogger<RouteEnrichmentService>.Instance);
    }

    [Fact]
    public async Task Found_links_are_filtered_deduped_and_appended_within_the_cap()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, user.Id);

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
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, user.Id);

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
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, user.Id); // SeedData.CreateRouteAsync validates by default

        var provider = new FakeProvider(["https://example.com/found"], null);
        var service = BuildService(db, provider);

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);

        var reloaded = await db.Routes.FindAsync(route.Id);
        reloaded!.ValidationStatus.ShouldBe(ReviewStatus.Approved);
    }

    [Fact]
    public async Task A_verified_topo_candidate_is_imported()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, user.Id);

        var provider = new FakeProvider([], "https://example.com/topo.jpg");
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, [1, 2, 3, 4], "image/jpeg");
        var service = BuildService(db, provider, new FakeTopoVerification(true), handler);

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);

        var topos = await db.Topos.Where(t => t.RouteId == route.Id).ToListAsync(TestContext.Current.CancellationToken);
        topos.Count.ShouldBe(1);
        topos[0].UploadedByUserId.ShouldBe(user.Id);
    }

    [Fact]
    public async Task An_unverified_topo_candidate_is_not_imported()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, user.Id);

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
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, user.Id, hasTopo: true);

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
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, user.Id);

        var provider = new FakeProvider([], "https://example.com/topo.svg");
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, [1, 2, 3], "image/svg+xml");
        var service = BuildService(db, provider, new FakeTopoVerification(true), handler);

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);

        var topos = await db.Topos.Where(t => t.RouteId == route.Id).ToListAsync(TestContext.Current.CancellationToken);
        topos.ShouldBeEmpty();
        var reloaded = await db.Routes.FindAsync(route.Id);
        reloaded!.EnrichedAt.ShouldNotBeNull(); // links/EnrichedAt still land even though the topo didn't
    }
}
