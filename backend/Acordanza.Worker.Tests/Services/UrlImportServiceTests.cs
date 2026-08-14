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

// Plan 31 — the Url kind's write paths through the shared spine: the submitted link and
// only the submitted link, first ascensionists matched but never created, the sector
// cross-check, and the duplicate short-circuit.
//
// The reader is faked wholesale rather than wired up with a fake fetcher: everything
// network- and model-shaped is already covered unit-tier (PageFetcherTests,
// PageParsingTests, WebPageAcceptanceTests), and what these tests are about is what the
// spine does with a RouteReading — which is exactly the seam plan 31 §2 introduced.
public class UrlImportServiceTests(WorkerFixture fixture) : WorkerTestBase(fixture)
{
    private sealed class FakeUrlReader(RouteReading reading) : IImportReader
    {
        public int CallCount { get; private set; }

        public RouteImportKind Kind => RouteImportKind.Url;

        public Task<RouteReading> ReadAsync(RouteImport import, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(reading);
        }
    }

    private const string PageUrl = "https://blog.example.com/apertura-la-loca";

    private static RouteReading CleanReading(
        string name = "La Loca",
        string grade = "6b+",
        IReadOnlyList<string>? openers = null,
        string? sectorMentioned = null,
        TopoCandidate? topo = null) =>
        new(new RouteCandidate(
                name, grade, 25, "«La Loca», 6b+, 25 m",
                "Placa técnica de 25 metros.", openers ?? [], sectorMentioned),
            topo, null);

    private RouteImportService BuildService(AppDbContext db, IImportReader reader) =>
        new(db,
            new AreaHierarchyService(db),
            [reader],
            Fixture.Services.GetRequiredService<MediaUploadService>(),
            Fixture.Services.GetRequiredService<BlobStorageService>(),
            NullLogger<RouteImportService>.Instance);

    private static async Task<RouteImport> SeedImportAsync(
        AppDbContext db, Area area, string userId, string url = PageUrl)
    {
        var import = new RouteImport
        {
            Kind = RouteImportKind.Url,
            SourceUrl = url,
            AreaId = area.Id,
            SubmittedByUserId = userId,
            Status = RouteImportStatus.Pending
        };
        db.RouteImports.Add(import);
        await db.SaveChangesAsync();
        return import;
    }

    private static async Task<Area> CreateAreaAsync(AppDbContext db, string? name = null) =>
        await SeedData.CreateAreaAsync(db, name ?? $"Peña Rubia {Guid.NewGuid():N}");

    private static async Task<FirstAscensionist> CreateOpenerAsync(AppDbContext db, string name)
    {
        var opener = new FirstAscensionist
        {
            Name = name,
            Slug = Slugify.ToSlug(name),
            ValidationStatus = ReviewStatus.Approved
        };
        db.FirstAscensionists.Add(opener);
        await db.SaveChangesAsync();
        return opener;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_clean_reading_with_no_match_creates_a_pending_route_linked_to_the_page()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        var import = await SeedImportAsync(db, area, user.Id);

        await BuildService(db, new FakeUrlReader(CleanReading())).ImportAsync(import.Id, Ct);

        var route = await db.Routes.AsNoTracking().Include(r => r.Links).SingleAsync(Ct);
        route.Name.ShouldBe("La Loca");
        route.Grade.ShouldBe("6b+");
        route.Description.ShouldBe("Placa técnica de 25 metros.");
        route.AddedByUserId.ShouldBe(user.Id);

        // The invariant, asserted explicitly because RoutesController.CreateRoute does the
        // opposite: the worker writes through AppDbContext directly precisely so
        // Validation.MarkValidated is unreachable from here. Nothing about the submitter's
        // role can change it — there are no permission claims on this path at all.
        route.ValidationStatus.ShouldBe(ReviewStatus.Pending);
        route.DecidedAt.ShouldBeNull();

        var link = route.Links.ShouldHaveSingleItem();
        link.Url.ShouldBe(PageUrl);
        link.Source.ShouldBe(LinkSource.Import);

        var reloaded = await db.RouteImports.AsNoTracking().FirstAsync(i => i.Id == import.Id, Ct);
        reloaded.Status.ShouldBe(RouteImportStatus.Completed);
        reloaded.RouteWasCreated.ShouldBeTrue();
        reloaded.RouteId.ShouldBe(route.Id);
    }

    [Fact]
    public async Task A_match_appends_exactly_one_link_and_changes_nothing_else()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);

        var existing = new ClimbingRoute
        {
            Name = "La Loca", Slug = "la-loca", AreaId = area.Id, Grade = "6a", Length = 30,
            Description = "Lo que escribió una persona",
            AddedByUserId = user.Id, ValidationStatus = ReviewStatus.Approved,
            DecidedAt = DateTimeOffset.UtcNow
        };
        db.Routes.Add(existing);
        await db.SaveChangesAsync(Ct);

        var import = await SeedImportAsync(db, area, user.Id);
        await BuildService(db, new FakeUrlReader(CleanReading(name: "LA LOCA"))).ImportAsync(import.Id, Ct);

        (await db.Routes.CountAsync(Ct)).ShouldBe(1);

        var reloadedRoute = await db.Routes.AsNoTracking().Include(r => r.Links).SingleAsync(Ct);
        // Neither the grade nor the description nor the validation state moves: appending a
        // link is not an edit of a human's record.
        reloadedRoute.Grade.ShouldBe("6a");
        reloadedRoute.Length.ShouldBe(30);
        reloadedRoute.Description.ShouldBe("Lo que escribió una persona");
        reloadedRoute.ValidationStatus.ShouldBe(ReviewStatus.Approved);
        reloadedRoute.Links.ShouldHaveSingleItem().Source.ShouldBe(LinkSource.Import);

        var reloaded = await db.RouteImports.AsNoTracking().FirstAsync(i => i.Id == import.Id, Ct);
        reloaded.RouteWasCreated.ShouldBeFalse();
        // The disagreement is recorded for a human to decide on, not acted on.
        reloaded.Outcome.ShouldBe("La vía ya existía. La página indica 6b+ y la ficha 6a");
    }

    [Fact]
    public async Task Re_submitting_the_same_url_short_circuits_without_reading_it_again()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);

        var first = await SeedImportAsync(db, area, user.Id);
        var reader = new FakeUrlReader(CleanReading());
        await BuildService(db, reader).ImportAsync(first.Id, Ct);

        var second = await SeedImportAsync(db, area, user.Id);
        await BuildService(db, reader).ImportAsync(second.Id, Ct);

        reader.CallCount.ShouldBe(1);
        (await db.Routes.CountAsync(Ct)).ShouldBe(1);

        var route = await db.Routes.AsNoTracking().SingleAsync(Ct);
        var reloaded = await db.RouteImports.AsNoTracking().FirstAsync(i => i.Id == second.Id, Ct);
        reloaded.Status.ShouldBe(RouteImportStatus.Completed);
        reloaded.RouteId.ShouldBe(route.Id);
        reloaded.RouteWasCreated.ShouldBeFalse();
    }

    // The short-circuit is keyed on the route still existing: RouteId is SetNull, so a
    // deleted route means the evidence outlived what it produced and the work is done again.
    [Fact]
    public async Task A_duplicate_url_whose_route_was_deleted_is_read_again()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);

        var first = await SeedImportAsync(db, area, user.Id);
        var reader = new FakeUrlReader(CleanReading());
        await BuildService(db, reader).ImportAsync(first.Id, Ct);

        db.Routes.RemoveRange(await db.Routes.ToListAsync(Ct));
        await db.SaveChangesAsync(Ct);

        var second = await SeedImportAsync(db, area, user.Id);
        await BuildService(db, reader).ImportAsync(second.Id, Ct);

        reader.CallCount.ShouldBe(2);
        (await db.Routes.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task First_ascensionists_are_matched_by_slug_and_attached()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        var opener = await CreateOpenerAsync(db, "Pedro Pons");
        var import = await SeedImportAsync(db, area, user.Id);

        await BuildService(db, new FakeUrlReader(CleanReading(openers: ["PEDRO  PONS"])))
            .ImportAsync(import.Id, Ct);

        var route = await db.Routes.AsNoTracking().Include(r => r.FirstAscensionists).SingleAsync(Ct);
        route.FirstAscensionists.ShouldHaveSingleItem().Id.ShouldBe(opener.Id);
    }

    // Never created — a write into a directory with its own section, claim flow and review
    // queue is not something a model's reading of an attacker-controllable page gets to do.
    [Fact]
    public async Task An_unknown_first_ascensionist_is_noted_and_not_created()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        var import = await SeedImportAsync(db, area, user.Id);

        await BuildService(db, new FakeUrlReader(CleanReading(openers: ["Pedro Pons"])))
            .ImportAsync(import.Id, Ct);

        (await db.FirstAscensionists.CountAsync(Ct)).ShouldBe(0);

        var reloaded = await db.RouteImports.AsNoTracking().FirstAsync(i => i.Id == import.Id, Ct);
        reloaded.Outcome.ShouldContain("No encontramos a \"Pedro Pons\" en el directorio");
    }

    // The "-2" suffix case: FirstAscensionist.Slug is globally unique, so two climbers of
    // the same name are `pedro-pons` and `pedro-pons-2`, and picking the first credits the
    // wrong one half the time.
    [Fact]
    public async Task An_ambiguous_first_ascensionist_is_skipped_rather_than_guessed()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        await CreateOpenerAsync(db, "Pedro Pons");
        db.FirstAscensionists.Add(new FirstAscensionist
        {
            Name = "Pedro Pons", Slug = "pedro-pons-2", ValidationStatus = ReviewStatus.Approved
        });
        await db.SaveChangesAsync(Ct);

        var import = await SeedImportAsync(db, area, user.Id);
        await BuildService(db, new FakeUrlReader(CleanReading(openers: ["Pedro Pons"])))
            .ImportAsync(import.Id, Ct);

        var route = await db.Routes.AsNoTracking().Include(r => r.FirstAscensionists).SingleAsync(Ct);
        route.FirstAscensionists.ShouldBeEmpty();

        var reloaded = await db.RouteImports.AsNoTracking().FirstAsync(i => i.Id == import.Id, Ct);
        reloaded.Outcome.ShouldContain("varios aperturistas");
    }

    // A cross-check, never an input: the route is still filed under the sector the
    // submitter picked, and the mismatch is a note for a human.
    [Fact]
    public async Task A_sector_the_page_names_that_does_not_match_is_noted_and_nothing_else()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db, "Peña Rubia");
        var import = await SeedImportAsync(db, area, user.Id);

        await BuildService(db, new FakeUrlReader(CleanReading(sectorMentioned: "Riglos")))
            .ImportAsync(import.Id, Ct);

        var route = await db.Routes.AsNoTracking().SingleAsync(Ct);
        route.AreaId.ShouldBe(area.Id);

        var reloaded = await db.RouteImports.AsNoTracking().FirstAsync(i => i.Id == import.Id, Ct);
        reloaded.Status.ShouldBe(RouteImportStatus.Completed);
        reloaded.Outcome.ShouldContain("La página menciona \"Riglos\"");
    }

    [Fact]
    public async Task A_sector_the_page_names_that_matches_the_breadcrumb_is_not_noted()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db, "Peña Rubia");
        var import = await SeedImportAsync(db, area, user.Id);

        await BuildService(db, new FakeUrlReader(CleanReading(sectorMentioned: "peña rubia")))
            .ImportAsync(import.Id, Ct);

        var reloaded = await db.RouteImports.AsNoTracking().FirstAsync(i => i.Id == import.Id, Ct);
        reloaded.Outcome.ShouldNotContain("revísalo");
    }

    [Fact]
    public async Task A_discarded_reading_writes_the_reason_and_creates_nothing()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        var import = await SeedImportAsync(db, area, user.Id);

        await BuildService(db, new FakeUrlReader(RouteReading.Discard(RouteImportOutcomes.NotAboutARoute)))
            .ImportAsync(import.Id, Ct);

        var reloaded = await db.RouteImports.AsNoTracking().FirstAsync(i => i.Id == import.Id, Ct);
        reloaded.Status.ShouldBe(RouteImportStatus.Discarded);
        reloaded.Outcome.ShouldBe(RouteImportOutcomes.NotAboutARoute);
        reloaded.RouteId.ShouldBeNull();

        (await db.Routes.CountAsync(Ct)).ShouldBe(0);
        (await db.Links.CountAsync(Ct)).ShouldBe(0);
    }

    // Failed, not Discarded — the row must never tell the submitter "we couldn't read it"
    // when the truth is "we never looked".
    [Fact]
    public async Task A_broken_reading_fails_rather_than_discarding()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        var import = await SeedImportAsync(db, area, user.Id);

        await BuildService(db, new FakeUrlReader(RouteReading.Broke(RouteImportOutcomes.Failed)))
            .ImportAsync(import.Id, Ct);

        var reloaded = await db.RouteImports.AsNoTracking().FirstAsync(i => i.Id == import.Id, Ct);
        reloaded.Status.ShouldBe(RouteImportStatus.Failed);
        reloaded.Outcome.ShouldBe(RouteImportOutcomes.Failed);
    }

    // A topo found on the page is attached under the route's own prefix exactly like an
    // uploaded one, with the submitter as its uploader — and there is no staged blob to
    // promote out of, since the bytes came off the page.
    [Fact]
    public async Task A_topo_found_on_the_page_is_attached_to_the_route()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        var import = await SeedImportAsync(db, area, user.Id);

        var topo = new TopoCandidate([0xFF, 0xD8, 0xFF, 0xD9], "image/jpeg", "topo-la-loca.jpg");
        await BuildService(db, new FakeUrlReader(CleanReading(topo: topo))).ImportAsync(import.Id, Ct);

        var route = await db.Routes.AsNoTracking().SingleAsync(Ct);
        var stored = await db.Topos.AsNoTracking().SingleAsync(Ct);
        stored.RouteId.ShouldBe(route.Id);
        stored.UploadedByUserId.ShouldBe(user.Id);
        stored.FileName.ShouldBe("topo-la-loca.jpg");
        stored.BlobName.ShouldBe($"{Slugify.ToSlug(area.Name)}/la-loca-01.jpg");

        // Nothing was staged, so nothing is repointed: the row's BlobName stays null and
        // SourceUrl remains the artifact.
        var reloaded = await db.RouteImports.AsNoTracking().FirstAsync(i => i.Id == import.Id, Ct);
        reloaded.BlobName.ShouldBeNull();
    }

    // Both kinds share one queue and one processor, so the pair has to drain in one run —
    // the spine picks the reader off the row's Kind, not off the message.
    [Fact]
    public async Task Each_kind_is_read_by_its_own_reader()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);

        var urlImport = await SeedImportAsync(db, area, user.Id);
        var imageImport = new RouteImport
        {
            Kind = RouteImportKind.Image,
            BlobName = $"{RouteImportKeys.StagingPrefix}{Guid.NewGuid():N}.jpg",
            ContentType = "image/jpeg",
            FileName = "topo.jpg",
            AreaId = area.Id,
            SubmittedByUserId = user.Id,
            Status = RouteImportStatus.Pending
        };
        db.RouteImports.Add(imageImport);
        await db.SaveChangesAsync(Ct);

        var urlReader = new FakeUrlReader(CleanReading(name: "La Loca"));
        var imageReader = new FakeImageReader(CleanReading(name: "El Cuervo"));
        var service = new RouteImportService(
            db,
            new AreaHierarchyService(db),
            [urlReader, imageReader],
            Fixture.Services.GetRequiredService<MediaUploadService>(),
            Fixture.Services.GetRequiredService<BlobStorageService>(),
            NullLogger<RouteImportService>.Instance);

        await service.ImportAsync(urlImport.Id, Ct);
        await service.ImportAsync(imageImport.Id, Ct);

        urlReader.CallCount.ShouldBe(1);
        imageReader.CallCount.ShouldBe(1);

        var names = await db.Routes.AsNoTracking().Select(r => r.Name).ToListAsync(Ct);
        names.ShouldBe(["La Loca", "El Cuervo"], ignoreOrder: true);

        // Only the Url kind writes a link, and it is the submitted one.
        (await db.Links.CountAsync(Ct)).ShouldBe(1);
    }

    private sealed class FakeImageReader(RouteReading reading) : IImportReader
    {
        public int CallCount { get; private set; }

        public RouteImportKind Kind => RouteImportKind.Image;

        public Task<RouteReading> ReadAsync(RouteImport import, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(reading);
        }
    }
}
