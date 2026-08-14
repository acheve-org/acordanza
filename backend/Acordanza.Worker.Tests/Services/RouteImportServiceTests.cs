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

// Plan 30 (route-from-topo), through plan 31's reader seam — the Image kind's write
// paths: gate discard, create-vs-match, blob promotion, and the four terminal states. Constructs RouteImportService directly with
// fake providers against this project's Postgres+Azurite fixture, exactly as
// RouteEnrichmentServiceTests does — no real Foundry call is ever made in CI. The pure
// acceptance predicate is unit-tier and lives in RouteImportAcceptanceTests.
public class RouteImportServiceTests(WorkerFixture fixture) : WorkerTestBase(fixture)
{
    // --- Fakes -------------------------------------------------------------------

    private sealed class FakeGate(bool isTopo, bool completed = true) : ITopoImportGateProvider
    {
        public int CallCount { get; private set; }

        public Task<TopoImportGateResult> IsTopoAsync(TopoImportGateContext context, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new TopoImportGateResult(isTopo, "fake", completed));
        }
    }

    private sealed class FakeExtraction(TopoExtractionResult result) : ITopoExtractionProvider
    {
        public int CallCount { get; private set; }
        public TopoExtractionContext? LastContext { get; private set; }

        public Task<TopoExtractionResult> ExtractAsync(TopoExtractionContext context, CancellationToken cancellationToken)
        {
            CallCount++;
            LastContext = context;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingExtraction : ITopoExtractionProvider
    {
        public Task<TopoExtractionResult> ExtractAsync(TopoExtractionContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Foundry is down");
    }

    // The model returning a clean, high-confidence read — the shape every test that isn't
    // specifically about the acceptance filter starts from.
    private static TopoExtractionResult CleanRead(string name = "La Loca", string grade = "6b+", int? length = 25) =>
        new(new ExtractedRoute(name, grade, length, "La Loca 6b+ 25m", "high"), false, null);

    // The Image kind's reader, wired with fakes and handed to the spine as the only
    // registered reader — the same shape production builds, minus Foundry.
    private RouteImportService BuildService(
        AppDbContext db, ITopoImportGateProvider gate, ITopoExtractionProvider? extraction = null)
    {
        var reader = new TopoImageReader(
            new AreaHierarchyService(db),
            gate,
            extraction ?? new FakeExtraction(CleanRead()),
            Fixture.Services.GetRequiredService<BlobStorageService>(),
            NullLogger<TopoImageReader>.Instance);

        return new RouteImportService(
            db,
            new AreaHierarchyService(db),
            [reader],
            Fixture.Services.GetRequiredService<MediaUploadService>(),
            Fixture.Services.GetRequiredService<BlobStorageService>(),
            NullLogger<RouteImportService>.Instance);
    }

    // A 1x1 JPEG is enough: nothing in the pipeline decodes the bytes, the providers are
    // faked, and Azurite doesn't care what a blob contains.
    private static readonly byte[] ImageBytes = [0xFF, 0xD8, 0xFF, 0xD9];

    private async Task<RouteImport> SeedImportAsync(AppDbContext db, Area area, string userId)
    {
        var blobStorage = Fixture.Services.GetRequiredService<BlobStorageService>();
        var blobName = $"{RouteImportKeys.StagingPrefix}{Guid.NewGuid():N}.jpg";
        await blobStorage.UploadAsync(blobName, new MemoryStream(ImageBytes), "image/jpeg");

        var import = new RouteImport
        {
            Kind = RouteImportKind.Image,
            BlobName = blobName,
            ContentType = "image/jpeg",
            FileName = "topo.jpg",
            AreaId = area.Id,
            SubmittedByUserId = userId,
            Status = RouteImportStatus.Pending
        };
        db.RouteImports.Add(import);
        await db.SaveChangesAsync();
        return import;
    }

    private async Task<bool> BlobExistsAsync(string blobName) =>
        await Fixture.Services.GetRequiredService<BlobStorageService>().DownloadAsync(blobName) is not null;

    // Respawn resets Postgres between tests but Azurite keeps its blobs, so two tests
    // sharing an area name would compute the same promoted blob name — and
    // UploadNumberedAsync derives the next number from db.Topos, which the reset just
    // emptied. A unique area per test keeps each one in its own blob prefix. (Azurite
    // also answers a failed If-None-Match with 409 rather than the 412 real Blob Storage
    // returns, so the race-retry inside UploadNumberedAsync wouldn't have caught it.)
    private static async Task<Area> CreateAreaAsync(AppDbContext db) =>
        await SeedData.CreateAreaAsync(db, $"Peña Rúa {Guid.NewGuid():N}");

    private static string TopoPrefix(Area area) => $"{Slugify.ToSlug(area.Name)}/la-loca-";

    [Fact]
    public async Task A_gate_rejection_discards_the_row_deletes_the_blob_and_creates_no_route()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        var import = await SeedImportAsync(db, area, user.Id);
        var stagedBlobName = import.BlobName!;

        var extraction = new FakeExtraction(CleanRead());
        await BuildService(db, new FakeGate(isTopo: false), extraction)
            .ImportAsync(import.Id, TestContext.Current.CancellationToken);

        var reloaded = await db.RouteImports.AsNoTracking()
            .FirstAsync(i => i.Id == import.Id, TestContext.Current.CancellationToken);
        reloaded.Status.ShouldBe(RouteImportStatus.Discarded);
        reloaded.Outcome.ShouldBe(RouteImportOutcomes.NotATopo);
        reloaded.RouteId.ShouldBeNull();
        reloaded.ProcessedAt.ShouldNotBeNull();

        // Short-circuited before the expensive call — that's the whole point of the gate.
        extraction.CallCount.ShouldBe(0);
        (await BlobExistsAsync(stagedBlobName)).ShouldBeFalse();
        (await db.Routes.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task A_clean_extraction_with_no_match_creates_a_pending_route()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        var import = await SeedImportAsync(db, area, user.Id);

        await BuildService(db, new FakeGate(isTopo: true), new FakeExtraction(CleanRead()))
            .ImportAsync(import.Id, TestContext.Current.CancellationToken);

        var route = await db.Routes.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        route.Name.ShouldBe("La Loca");
        route.Grade.ShouldBe("6b+");
        route.Length.ShouldBe(25);
        route.Slug.ShouldBe("la-loca");
        route.AreaId.ShouldBe(area.Id);
        route.AddedByUserId.ShouldBe(user.Id);

        // The §4 invariant, asserted explicitly because RoutesController.CreateRoute does
        // the opposite: the worker writes through AppDbContext directly precisely so
        // Validation.MarkValidated is unreachable from here. The uploader's role is
        // irrelevant — there are no permission claims on this path at all, which is
        // exactly what makes "even an admin's photo lands as Pending" structural rather
        // than a check someone could later relax.
        route.ValidationStatus.ShouldBe(ReviewStatus.Pending);
        route.DecidedAt.ShouldBeNull();
        route.DecidedByUserId.ShouldBeNull();

        var reloaded = await db.RouteImports.AsNoTracking()
            .FirstAsync(i => i.Id == import.Id, TestContext.Current.CancellationToken);
        reloaded.Status.ShouldBe(RouteImportStatus.Completed);
        reloaded.RouteWasCreated.ShouldBeTrue();
        reloaded.RouteId.ShouldBe(route.Id);
    }

    [Fact]
    public async Task The_staged_blob_is_promoted_to_the_routes_own_prefix()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        var import = await SeedImportAsync(db, area, user.Id);
        var stagedBlobName = import.BlobName!;

        await BuildService(db, new FakeGate(isTopo: true), new FakeExtraction(CleanRead()))
            .ImportAsync(import.Id, TestContext.Current.CancellationToken);

        var promoted = $"{TopoPrefix(area)}01.jpg";
        var topo = await db.Topos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        topo.BlobName.ShouldBe(promoted);
        // The actual uploader, not the route's creator — cleaner than enrichment, which
        // had to attribute to route.AddedByUserId because no human was in the loop.
        topo.UploadedByUserId.ShouldBe(user.Id);

        (await BlobExistsAsync(promoted)).ShouldBeTrue();
        (await BlobExistsAsync(stagedBlobName)).ShouldBeFalse();

        // The row follows the image, so it still points at something live.
        var reloaded = await db.RouteImports.AsNoTracking()
            .FirstAsync(i => i.Id == import.Id, TestContext.Current.CancellationToken);
        reloaded.BlobName.ShouldBe(promoted);
    }

    [Fact]
    public async Task A_matching_route_gets_the_topo_and_nothing_else()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);

        var existing = new ClimbingRoute
        {
            Name = "La Loca",
            Slug = "la-loca",
            AreaId = area.Id,
            Grade = "6a",
            Length = 30,
            AddedByUserId = user.Id,
            ValidationStatus = ReviewStatus.Approved,
            DecidedAt = DateTimeOffset.UtcNow
        };
        db.Routes.Add(existing);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var import = await SeedImportAsync(db, area, user.Id);

        // Case differs from the stored name — matching is on the slug, so it still matches.
        await BuildService(db, new FakeGate(isTopo: true), new FakeExtraction(CleanRead(name: "LA LOCA")))
            .ImportAsync(import.Id, TestContext.Current.CancellationToken);

        (await db.Routes.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);

        var reloadedRoute = await db.Routes.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        // A grade disagreement does not rewrite the route, and adding a photo is not an
        // edit of its data — so neither Grade nor ValidationStatus moves.
        reloadedRoute.Grade.ShouldBe("6a");
        reloadedRoute.Length.ShouldBe(30);
        reloadedRoute.ValidationStatus.ShouldBe(ReviewStatus.Approved);

        var reloaded = await db.RouteImports.AsNoTracking()
            .FirstAsync(i => i.Id == import.Id, TestContext.Current.CancellationToken);
        reloaded.Status.ShouldBe(RouteImportStatus.Completed);
        reloaded.RouteWasCreated.ShouldBeFalse();
        reloaded.RouteId.ShouldBe(existing.Id);
        // The disagreement is recorded for a human to decide on, not acted on.
        reloaded.Outcome.ShouldBe("La vía ya existía. El topo indica 6b+ y la ficha 6a");

        (await db.Topos.CountAsync(t => t.RouteId == existing.Id, TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task A_match_against_a_route_that_already_has_topos_still_appends()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);

        var existing = new ClimbingRoute
        {
            Name = "La Loca", Slug = "la-loca", AreaId = area.Id, Grade = "6b+", Length = 25,
            AddedByUserId = user.Id, ValidationStatus = ReviewStatus.Approved
        };
        db.Routes.Add(existing);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.Topos.Add(new Topo
        {
            RouteId = existing.Id,
            BlobName = $"{TopoPrefix(area)}01.jpg",
            ContentType = "image/jpeg",
            FileName = "topo.jpg",
            UploadedByUserId = user.Id
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var import = await SeedImportAsync(db, area, user.Id);
        await BuildService(db, new FakeGate(isTopo: true), new FakeExtraction(CleanRead()))
            .ImportAsync(import.Id, TestContext.Current.CancellationToken);

        // Enrichment skips a route that already has topos because nobody asked for the
        // import; here a human deliberately uploaded, so it appends under the next number.
        var blobNames = await db.Topos.AsNoTracking()
            .Where(t => t.RouteId == existing.Id)
            .Select(t => t.BlobName)
            .ToListAsync(TestContext.Current.CancellationToken);
        blobNames.ShouldBe([$"{TopoPrefix(area)}01.jpg", $"{TopoPrefix(area)}02.jpg"], ignoreOrder: true);
    }

    // §4's exact-area rule: the uploader picked *this* sector, so a same-named route in a
    // sibling is a different route, not a match.
    [Fact]
    public async Task A_same_named_route_in_a_sibling_area_does_not_match()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var target = await CreateAreaAsync(db);
        var sibling = await CreateAreaAsync(db);

        db.Routes.Add(new ClimbingRoute
        {
            Name = "La Loca", Slug = "la-loca", AreaId = sibling.Id, Grade = "6a", Length = 30,
            AddedByUserId = user.Id, ValidationStatus = ReviewStatus.Approved
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var import = await SeedImportAsync(db, target, user.Id);
        await BuildService(db, new FakeGate(isTopo: true), new FakeExtraction(CleanRead()))
            .ImportAsync(import.Id, TestContext.Current.CancellationToken);

        (await db.Routes.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
        var created = await db.Routes.AsNoTracking()
            .SingleAsync(r => r.AreaId == target.Id, TestContext.Current.CancellationToken);
        created.ValidationStatus.ShouldBe(ReviewStatus.Pending);
    }

    // An incomplete call is the pipeline falling over, not an answer about the image, so
    // it must never masquerade as a discard — the uploader would be told their topo was
    // unreadable when nothing ever looked at it.
    [Fact]
    public async Task An_incomplete_extraction_fails_rather_than_discarding()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        var import = await SeedImportAsync(db, area, user.Id);
        var stagedBlobName = import.BlobName!;

        var incomplete = new TopoExtractionResult(null, false, null, Completed: false);
        await BuildService(db, new FakeGate(isTopo: true), new FakeExtraction(incomplete))
            .ImportAsync(import.Id, TestContext.Current.CancellationToken);

        var reloaded = await db.RouteImports.AsNoTracking()
            .FirstAsync(i => i.Id == import.Id, TestContext.Current.CancellationToken);
        reloaded.Status.ShouldBe(RouteImportStatus.Failed);
        reloaded.Outcome.ShouldBe(RouteImportOutcomes.Failed);
        (await db.Routes.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);

        // Kept, unlike on a discard: a Failed row is retryable, and re-running it needs
        // the image. (The _imports/ lifecycle rule is what collects it if nobody does.)
        (await BlobExistsAsync(stagedBlobName)).ShouldBeTrue();
    }

    [Fact]
    public async Task An_incomplete_gate_fails_without_calling_the_extraction()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        var import = await SeedImportAsync(db, area, user.Id);

        var extraction = new FakeExtraction(CleanRead());
        await BuildService(db, new FakeGate(isTopo: true, completed: false), extraction)
            .ImportAsync(import.Id, TestContext.Current.CancellationToken);

        extraction.CallCount.ShouldBe(0);
        var reloaded = await db.RouteImports.AsNoTracking()
            .FirstAsync(i => i.Id == import.Id, TestContext.Current.CancellationToken);
        reloaded.Status.ShouldBe(RouteImportStatus.Failed);
    }

    [Fact]
    public async Task A_missing_staged_blob_fails_rather_than_throwing()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        var import = await SeedImportAsync(db, area, user.Id);
        await Fixture.Services.GetRequiredService<BlobStorageService>().DeleteAsync(import.BlobName!);

        var gate = new FakeGate(isTopo: true);
        await BuildService(db, gate).ImportAsync(import.Id, TestContext.Current.CancellationToken);

        gate.CallCount.ShouldBe(0);
        var reloaded = await db.RouteImports.AsNoTracking()
            .FirstAsync(i => i.Id == import.Id, TestContext.Current.CancellationToken);
        reloaded.Status.ShouldBe(RouteImportStatus.Failed);
        reloaded.Outcome.ShouldBe(RouteImportOutcomes.ImageGone);
    }

    // Idempotency: a redelivered or duplicated message must not import the same photo twice.
    [Fact]
    public async Task An_already_decided_import_is_skipped()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        var import = await SeedImportAsync(db, area, user.Id);

        var gate = new FakeGate(isTopo: true);
        var service = BuildService(db, gate, new FakeExtraction(CleanRead()));
        await service.ImportAsync(import.Id, TestContext.Current.CancellationToken);
        await service.ImportAsync(import.Id, TestContext.Current.CancellationToken);

        gate.CallCount.ShouldBe(1);
        (await db.Routes.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        (await db.Topos.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    // The sector is given, not inferred — and it's passed into the extraction prompt as
    // context, which is what makes every other answer better (plan 29 §2's lesson).
    [Fact]
    public async Task The_extraction_is_given_the_sector_breadcrumb()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        var import = await SeedImportAsync(db, area, user.Id);

        var extraction = new FakeExtraction(CleanRead());
        await BuildService(db, new FakeGate(isTopo: true), extraction)
            .ImportAsync(import.Id, TestContext.Current.CancellationToken);

        extraction.LastContext.ShouldNotBeNull();
        extraction.LastContext.AreaBreadcrumb.ShouldBe(area.Name);
    }

    // A provider blowing up must propagate, so the queue processor can retry it and, at
    // the end of the road, mark the row Failed — see RouteImportQueueProcessorTests.
    [Fact]
    public async Task A_throwing_provider_propagates_and_leaves_the_row_pending()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        var import = await SeedImportAsync(db, area, user.Id);

        var service = BuildService(db, new FakeGate(isTopo: true), new ThrowingExtraction());
        await Should.ThrowAsync<InvalidOperationException>(
            () => service.ImportAsync(import.Id, TestContext.Current.CancellationToken));

        var reloaded = await db.RouteImports.AsNoTracking()
            .FirstAsync(i => i.Id == import.Id, TestContext.Current.CancellationToken);
        reloaded.Status.ShouldBe(RouteImportStatus.Pending);
    }
}
