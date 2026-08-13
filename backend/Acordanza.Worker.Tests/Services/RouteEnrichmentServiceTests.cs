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
    private sealed class FakeProvider(EnrichmentResult result) : IRouteEnrichmentProvider
    {
        public int CallCount { get; private set; }
        public RouteEnrichmentContext? LastContext { get; private set; }

        public FakeProvider(List<string> links, string? topoImageUrl)
            : this(new EnrichmentResult(links.Select(url => HighConfidence(url)).ToList(), null, topoImageUrl))
        {
        }

        public Task<EnrichmentResult> FindAsync(RouteEnrichmentContext context, CancellationToken cancellationToken)
        {
            CallCount++;
            LastContext = context;
            return Task.FromResult(result);
        }
    }

    // The service only ever appends a high-confidence link carrying an evidence quote
    // (plan 29 §2), so every fixture that isn't specifically testing that filter goes
    // through here rather than restating the two fields.
    private static EnrichmentLink HighConfidence(string url, string kind = "first-ascent") =>
        new(url, "Título", kind, "…la vía en este sector…", "high");

    // Every candidate link is now checked for reachability before it is appended (§1b), so
    // the default stub has to answer like a live page — the topo tests pass their own.
    private static StubHttpMessageHandler ReachablePage() =>
        new(HttpStatusCode.OK, [1], "text/html");

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
        var httpClientFactory = new StubHttpClientFactory(httpHandler ?? ReachablePage());

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

        var tracked = await db.Routes.Include(r => r.Links).FirstAsync(r => r.Id == route.Id, TestContext.Current.CancellationToken);
        tracked.Links.Add(new Link { Url = "https://example.com/existing" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var found = new List<string>
        {
            "https://example.com/existing", // duplicate — dropped
            "javascript:alert(1)",          // scheme not allowed — dropped by LinkRules
            "https://example.com/new"
        };
        var provider = new FakeProvider(found, null);
        var service = BuildService(db, provider);

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);

        var reloaded = await db.Routes.Include(r => r.Links).FirstAsync(r => r.Id == route.Id, TestContext.Current.CancellationToken);
        reloaded.Links.Select(l => l.Url).ShouldBe(["https://example.com/existing", "https://example.com/new"]);
        reloaded.EnrichedAt.ShouldNotBeNull();

        // Plan 29 §6: provenance, so a bad batch is identifiable (and deletable) later. The
        // pre-existing link is untouched.
        reloaded.Links.Single(l => l.Url.EndsWith("existing")).Source.ShouldBe(LinkSource.Human);
        reloaded.Links.Single(l => l.Url.EndsWith("new")).Source.ShouldBe(LinkSource.Enrichment);
    }

    [Fact]
    public async Task Only_high_confidence_links_carrying_an_evidence_quote_are_appended()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, user.Id);

        var provider = new FakeProvider(new EnrichmentResult(
        [
            new EnrichmentLink("https://a.example/si", "A", "first-ascent", "…la vía en el sector…", "high"),
            new EnrichmentLink("https://b.example/quizas", "B", "first-ascent", "…la vía…", "medium"),
            new EnrichmentLink("https://c.example/sin-cita", "C", "first-ascent", "  ", "high")
        ], null, null));
        var service = BuildService(db, provider);

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);

        var reloaded = await db.Routes.Include(r => r.Links).FirstAsync(r => r.Id == route.Id, TestContext.Current.CancellationToken);
        reloaded.Links.Select(l => l.Url).ShouldBe(["https://a.example/si"]);
    }

    [Fact]
    public async Task Links_are_ordered_by_kind_and_capped_at_one_per_host_and_four_per_run()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, user.Id);

        var provider = new FakeProvider(new EnrichmentResult(
        [
            // The case that made plan 29 necessary: five results from one site used to fill
            // five of the ten link slots.
            HighConfidence("https://blog.example/uno", "sector"),
            HighConfidence("https://blog.example/dos", "sector"),
            HighConfidence("https://blog.example/tres", "sector"),
            HighConfidence("https://blog.example/cuatro", "sector"),
            HighConfidence("https://blog.example/cinco", "sector"),
            HighConfidence("https://video.example/v", "video"),
            HighConfidence("https://guia.example/g", "guidebook"),
            HighConfidence("https://apertura.example/fa", "first-ascent"),
            HighConfidence("https://topo.example/t", "topo"),
            HighConfidence("https://otro.example/o", "other")
        ], null, null));
        var service = BuildService(db, provider);

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);

        var reloaded = await db.Routes.Include(r => r.Links).FirstAsync(r => r.Id == route.Id, TestContext.Current.CancellationToken);
        reloaded.Links.Select(l => l.Url).ShouldBe(
        [
            "https://topo.example/t",
            "https://guia.example/g",
            "https://apertura.example/fa",
            "https://video.example/v"
        ]);
    }

    [Fact]
    public async Task An_unreachable_or_non_html_link_is_dropped_before_it_is_appended()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, user.Id);

        var provider = new FakeProvider([ "https://example.com/muerto" ], null);
        var service = BuildService(db, provider, httpHandler: new StubHttpMessageHandler(HttpStatusCode.NotFound, null, null));

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);

        var reloaded = await db.Routes.Include(r => r.Links).FirstAsync(r => r.Id == route.Id, TestContext.Current.CancellationToken);
        reloaded.Links.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_topo_page_without_an_image_url_is_appended_as_a_link_and_imports_no_topo()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, user.Id);

        var provider = new FakeProvider(new EnrichmentResult([], "https://example.com/resenya", null));
        var service = BuildService(db, provider, new FakeTopoVerification(true));

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);

        var reloaded = await db.Routes.Include(r => r.Links).FirstAsync(r => r.Id == route.Id, TestContext.Current.CancellationToken);
        reloaded.Links.Select(l => l.Url).ShouldBe(["https://example.com/resenya"]);
        reloaded.Links.Single().Source.ShouldBe(LinkSource.Enrichment);
        (await db.Topos.Where(t => t.RouteId == route.Id).ToListAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_that_ended_on_a_warning_path_leaves_EnrichedAt_null_so_the_button_can_be_pressed_again()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, user.Id);

        var provider = new FakeProvider(new EnrichmentResult([], null, null, Completed: false));
        var service = BuildService(db, provider);

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);

        var reloaded = await db.Routes.FindAsync([route.Id], TestContext.Current.CancellationToken);
        reloaded!.EnrichedAt.ShouldBeNull();

        // ...and because EnrichedAt is still null, an unforced re-run really does call the
        // provider again rather than short-circuiting on idempotency.
        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);
        provider.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task The_context_handed_to_the_provider_carries_the_first_ascensionists()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, user.Id);

        var opener = new FirstAscensionist { Name = $"Aperturista {Guid.NewGuid():N}" };
        var tracked = await db.Routes.Include(r => r.FirstAscensionists)
            .FirstAsync(r => r.Id == route.Id, TestContext.Current.CancellationToken);
        tracked.FirstAscensionists.Add(opener);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = new FakeProvider([], null);
        var service = BuildService(db, provider);

        await service.EnrichAsync(route.Id, force: false, TestContext.Current.CancellationToken);

        // The strongest search key the row holds, and plan 18 never passed it.
        provider.LastContext!.FirstAscensionists.ShouldBe([opener.Name]);
        provider.LastContext.Length.ShouldBe(tracked.Length);
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

        var reloaded = await db.Routes.FindAsync([route.Id], TestContext.Current.CancellationToken);
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
        var reloaded = await db.Routes.FindAsync([route.Id], TestContext.Current.CancellationToken);
        reloaded!.EnrichedAt.ShouldNotBeNull(); // links/EnrichedAt still land even though the topo didn't
    }
}
