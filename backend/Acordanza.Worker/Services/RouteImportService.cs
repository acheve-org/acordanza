using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Acordanza.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Worker.Services;

// Every user-facing string this pipeline can write. Spanish, like every { error } payload
// the frontend surfaces — RouteImport.Outcome is read by the person who submitted the
// photo or the link. Gathered here rather than inlined so the tests assert against the
// same constants production writes, and so the whole vocabulary of "why did this not
// work" is one screen.
public static class RouteImportOutcomes
{
    // Image kind (plan 30).
    public const string NotATopo = "No parece un topo de escalada";
    public const string NoRouteName = "El topo no indica el nombre de la vía";
    public const string MultipleRoutes = "El topo muestra varias vías y ninguna es claramente la principal";
    public const string UnreadableGrade = "No se pudo leer un grado válido (escala francesa)";
    public const string ImageGone = "La imagen ya no está disponible, vuelve a subirla";

    // Url kind (plan 31).
    public const string PageUnreachable = "No se pudo abrir la página, o no es una página web legible";
    public const string NoReadableText = "La página no tiene texto legible; puede que necesite JavaScript";
    public const string NotAboutARoute = "La página no habla de una vía de escalada";
    public const string MultipleRoutesOnPage = "La página describe varias vías y ninguna es claramente la principal";
    public const string NoRouteNameOnPage = "No se pudo leer el nombre de la vía";
    public const string UnreadableGradeOnPage = "No se pudo leer un grado válido";

    // Both kinds.
    public const string LowConfidence = "No se pudo leer con suficiente claridad";
    public const string Failed = "No se pudo completar la lectura, inténtalo de nuevo";
    public const string RouteCreated = "Vía creada y pendiente de validación";
    public const string RouteMatched = "La vía ya existía; se ha añadido lo que faltaba";
    // The same fact, without the reassuring half, for when there are notes to follow it:
    // «La vía ya existía. El topo indica 6b+ y la ficha 6a».
    public const string RouteExisted = "La vía ya existía";
}

// The spine both import kinds converge on (plan 31 §2): take a RouteReading from whichever
// reader matches the row's kind, then match-or-create, attach the topo, append the link,
// and write the outcome. Knows nothing about images or HTTP — that seam is what keeps the
// Url kind from reimplementing plan 30's match-or-create, and what keeps both kinds honest
// about the Discarded/Failed distinction (only the reader can tell them apart, and
// RouteReading forces it to say which).
//
// Writes through AppDbContext directly, never through RoutesController.CreateRoute, so
// Validation.MarkValidated is structurally out of the path. This matters: CreateRoute
// auto-approves when the creator can validate the area, and an admin pasting a link must
// still get a route somebody has *read* before it goes live. The worker never approves.
public class RouteImportService(
    AppDbContext db,
    AreaHierarchyService areaHierarchy,
    IEnumerable<IImportReader> readers,
    MediaUploadService mediaUpload,
    BlobStorageService blobStorage,
    ILogger<RouteImportService> logger)
{
    public async Task ImportAsync(int importId, CancellationToken cancellationToken = default)
    {
        var import = await db.RouteImports.FirstOrDefaultAsync(i => i.Id == importId, cancellationToken);
        if (import is null)
        {
            logger.LogWarning("route import skipped: import {ImportId} not found", importId);
            return;
        }

        if (import.Status != RouteImportStatus.Pending)
        {
            // Idempotency: a redelivered/duplicate message finds this already decided.
            logger.LogInformation("route import skipped: import {ImportId} already {Status}", importId, import.Status);
            return;
        }

        // Cheap, and it makes a double-click or a re-paste harmless: an identical canonical
        // URL that already produced a route completes against the same route, spending
        // nothing at all. Before the reader, so not even the fetch happens.
        if (import.Kind == RouteImportKind.Url && await FindEarlierImportAsync(import, cancellationToken) is { } earlier)
        {
            logger.LogInformation("route import {ImportId}: same URL already imported as {RouteId}", importId, earlier.RouteId);
            import.RouteId = earlier.RouteId;
            import.RouteWasCreated = false;
            await FinishAsync(import, RouteImportStatus.Completed, RouteImportOutcomes.RouteMatched, cancellationToken);
            return;
        }

        var reader = readers.FirstOrDefault(r => r.Kind == import.Kind);
        if (reader is null)
        {
            // Unreachable with the DI graph as registered; a Failed row rather than a throw
            // so a mis-registration doesn't burn five redeliveries per message.
            logger.LogError("route import {ImportId}: no reader registered for kind {Kind}", importId, import.Kind);
            await FinishAsync(import, RouteImportStatus.Failed, RouteImportOutcomes.Failed, cancellationToken);
            return;
        }

        var reading = await reader.ReadAsync(import, cancellationToken);

        if (reading.Failed)
        {
            await FinishAsync(import, RouteImportStatus.Failed, reading.DiscardReason ?? RouteImportOutcomes.Failed, cancellationToken);
            LogStages(import, "read-failed", extracted: false, matched: false, created: false);
            return;
        }

        if (reading.Route is not { } candidate)
        {
            await DiscardAsync(import, reading.DiscardReason ?? RouteImportOutcomes.Failed, cancellationToken);
            LogStages(import, "read-discarded", extracted: false, matched: false, created: false);
            return;
        }

        // Matching is deterministic and needs no fuzzy logic, because the sector was
        // given: Slug is already unique per AreaId and regenerated whenever the name or
        // area changes, so it is precisely the right key. Scoped to the given area
        // *exactly*, not its descendants — the submitter picked that sector explicitly.
        var candidateSlug = Slugify.ToSlug(candidate.Name);
        var existing = await db.Routes
            .Include(r => r.Topos)
            .Include(r => r.Links)
            .Include(r => r.FirstAscensionists)
            .FirstOrDefaultAsync(r => r.AreaId == import.AreaId && r.Slug == candidateSlug, cancellationToken);

        var route = existing ?? await CreateRouteAsync(import, candidate, cancellationToken);
        var created = existing is null;

        var notes = new List<string>();
        var areas = await areaHierarchy.GetAsync();

        if (reading.Topo is { } topo)
        {
            var attached = await AttachTopoAsync(import, route, areas.SlugPath(route.AreaId), topo, cancellationToken);
            if (!attached)
            {
                // Nothing free to name the blob, or the upload lost every race. The route
                // (if it was created) still stands — it's the useful half — but the row has
                // to say the photo didn't land.
                logger.LogWarning("route import {ImportId}: topo attach failed for route {RouteId}", importId, route.Id);
            }
        }

        if (import.Kind == RouteImportKind.Url)
        {
            AppendSubmittedLink(import, route);
            notes.AddRange(await MatchFirstAscensionistsAsync(route, candidate, cancellationToken));
            if (SectorMismatchNote(candidate, areas, import.AreaId) is { } mismatch)
            {
                notes.Add(mismatch);
            }
        }

        if (!created && GradeDisagreement(candidate, route, import.Kind) is { } disagreement)
        {
            notes.Add(disagreement);
        }

        import.RouteId = route.Id;
        import.RouteWasCreated = created;
        var headline = created
            ? RouteImportOutcomes.RouteCreated
            : notes.Count > 0 ? RouteImportOutcomes.RouteExisted : RouteImportOutcomes.RouteMatched;
        var outcome = string.Join(". ", notes.Prepend(headline));
        await FinishAsync(import, RouteImportStatus.Completed, outcome, cancellationToken);

        // The whole decision chain as one line — the direct analogue of plan 29 §7's stage
        // counts, and the thing that says whether to tune a prompt or a filter.
        LogStages(import, "completed", extracted: true, matched: !created, created: created);
    }

    // ClimbingRoute.Length is a non-nullable int with no "unknown" value, so an artifact
    // that doesn't state a length leaves the column default of 0. Note that 0 is *below*
    // RoutesController.ValidateLength's floor (> 5): a reviewer opening such a route in
    // the edit form has to supply a length before they can save it. That is the intended
    // behaviour — the source didn't say, so a human must — but it is why both extraction
    // prompts ask for null rather than an estimate, and why an out-of-range value the
    // model did return is dropped here rather than written through.
    internal static int AcceptableLength(int? length) => length is > 5 and < 3000 ? length.Value : 0;

    private async Task<RouteImport?> FindEarlierImportAsync(RouteImport import, CancellationToken cancellationToken) =>
        await db.RouteImports
            .AsNoTracking()
            .Where(i => i.Id != import.Id
                && i.Kind == RouteImportKind.Url
                && i.SourceUrl == import.SourceUrl
                && i.Status == RouteImportStatus.Completed
                && i.RouteId != null
                // The route may since have been deleted — RouteId is SetNull, so a null
                // here means the evidence outlived what it produced and this run should
                // do the work again rather than complete against nothing.
                && i.Route != null)
            .OrderByDescending(i => i.ProcessedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<ClimbingRoute> CreateRouteAsync(
        RouteImport import, RouteCandidate candidate, CancellationToken cancellationToken)
    {
        var route = new ClimbingRoute
        {
            Name = candidate.Name,
            // Goes through the same uniqueness loop RoutesController.CreateRoute uses
            // (hoisted to Core for exactly this second caller). We know the plain slug is
            // free — nothing matched it just above — but deriving it a second way here is
            // how the two paths would drift.
            Slug = await RouteSlugs.GenerateUniqueAsync(db, import.AreaId, candidate.Name, cancellationToken: cancellationToken),
            AreaId = import.AreaId,
            Grade = candidate.Grade,
            Length = candidate.Length,
            // Empty for the Image kind — a photo of a topo says nothing a summary could be
            // written from. A page does.
            Description = candidate.Description,
            // The submitter, so the route lands in their /my-acordanza/contributions and
            // stays visible to them under plan 11's rule while it waits for review.
            AddedByUserId = import.SubmittedByUserId,
            // Left at Pending explicitly, including when the submitter is an admin. This is
            // the invariant the integration tests assert: CreateRoute does the opposite,
            // and the whole reason this write bypasses it.
            ValidationStatus = ReviewStatus.Pending
        };

        db.Routes.Add(route);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "route import created route {RouteId} '{Name}' ({Grade}) in area {AreaId}",
            route.Id, route.Name, route.Grade, route.AreaId);
        return route;
    }

    // The submitted URL, and only ever the submitted URL — never one found on the page,
    // never one the model mentioned (plan 31 §5). This is the concrete bound on prompt
    // injection, and it costs nothing: the user pointed us at one page, and that page is
    // what the route should link to.
    private void AppendSubmittedLink(RouteImport import, ClimbingRoute route)
    {
        var url = import.SourceUrl!;
        if (route.Links.Any(l => string.Equals(l.Url, url, StringComparison.OrdinalIgnoreCase))) return;

        // LinkRules.MaxLinks is the *human-entry* contract shared with RoutesController, so
        // it is honoured rather than raised: a route already carrying ten links doesn't get
        // an eleventh from a machine.
        if (route.Links.Count >= LinkRules.MaxLinks) return;

        route.Links.Add(new Link { RouteId = route.Id, Url = url, Source = LinkSource.Import });
    }

    // Matched, never created (plan 31 §5/§8). A blog post naming the openers is the single
    // most valuable thing a page carries that a topo photo does not — but creating
    // FirstAscensionist rows from a model's reading of an attacker-controllable page is a
    // write into a directory with its own section, its own claim flow and its own review
    // queue. So: match exactly on slug, attach the hits, note the misses for a human.
    private async Task<List<string>> MatchFirstAscensionistsAsync(
        ClimbingRoute route, RouteCandidate candidate, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        foreach (var name in candidate.FirstAscensionists ?? [])
        {
            var slug = Slugify.ToSlug(name);
            if (slug.Length == 0) continue;

            // FirstAscensionist.Slug is globally unique, so two climbers of the same name
            // are stored as `pedro-pons` and `pedro-pons-2` — matching on the slug alone
            // would silently credit whichever got there first. So: pull the whole suffix
            // family and keep the ones whose *name* actually slugifies to this one, which
            // excludes a genuinely different `pedro-pons-ramos` that shares the prefix.
            var family = await db.FirstAscensionists
                .Where(fa => fa.Slug == slug || fa.Slug.StartsWith(slug + "-"))
                .ToListAsync(cancellationToken);
            var matches = family.Where(fa => Slugify.ToSlug(fa.Name) == slug).ToList();

            if (matches.Count == 0)
            {
                notes.Add($"No encontramos a \"{name}\" en el directorio");
                continue;
            }

            // The "-2" suffix case: two records share a name, and picking one is a guess
            // that credits the wrong climber. Skipped, not guessed.
            if (matches.Count > 1)
            {
                notes.Add($"Hay varios aperturistas llamados \"{name}\"; no se ha añadido ninguno");
                continue;
            }

            if (route.FirstAscensionists.All(fa => fa.Id != matches[0].Id))
            {
                route.FirstAscensionists.Add(matches[0]);
            }
        }
        return notes;
    }

    // Not a discard: crag naming is inconsistent enough — a post says «Peña Rubia» where
    // the log has a Sector Norte under a Peña Rubia parent, or the reverse — that a false
    // discard here would be infuriating and frequent. A note, for a human to judge.
    private static string? SectorMismatchNote(RouteCandidate candidate, AreaHierarchy areas, int areaId)
    {
        if (candidate.SectorMentioned is not { Length: > 0 } mentioned) return null;

        var breadcrumb = areas.NamePath(areaId);
        var mentionedSlug = Slugify.ToSlug(mentioned);
        if (mentionedSlug.Length == 0) return null;

        var breadcrumbMatches = breadcrumb
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(segment =>
            {
                var segmentSlug = Slugify.ToSlug(segment);
                return segmentSlug.Contains(mentionedSlug) || mentionedSlug.Contains(segmentSlug);
            });

        return breadcrumbMatches
            ? null
            : $"La página menciona \"{mentioned}\" y el sector elegido es \"{breadcrumb}\" — revísalo";
    }

    // A grade disagreement does not block the match and does not rewrite the route:
    // silently correcting a human-entered grade from a photograph or a blog post is the
    // wrong default in a catalogue whose whole point is that people entered it. Record it
    // and let a human decide. ValidationStatus is likewise untouched — adding a photo or a
    // link is not an edit of the route's data, and UploadTopo doesn't invalidate a route
    // today either.
    private static string? GradeDisagreement(RouteCandidate candidate, ClimbingRoute route, RouteImportKind kind)
    {
        if (string.Equals(candidate.Grade, route.Grade, StringComparison.OrdinalIgnoreCase)) return null;

        var source = kind == RouteImportKind.Image ? "El topo" : "La página";
        return $"{source} indica {candidate.Grade} y la ficha {route.Grade}";
    }

    // Blob Storage has no rename, so an Image import's promotion is really re-upload +
    // delete: the staged bytes are already in memory (they fed the vision calls), so this
    // writes them under the route's own prefix exactly as a human UploadTopo would, then
    // drops the staged copy and repoints the row at the promoted name. A Url import has no
    // staged blob — the bytes came off the page — so there is nothing to drop.
    private async Task<bool> AttachTopoAsync(
        RouteImport import, ClimbingRoute route, string areaSlugPath, TopoCandidate topo,
        CancellationToken cancellationToken)
    {
        var extension = ExtensionFor(import, topo);
        var blobPrefix = $"{areaSlugPath}/{Slugify.ToSlug(route.Name)}-";
        var usedBlobNames = await db.Topos.AsNoTracking()
            .Where(t => t.BlobName.StartsWith(blobPrefix))
            .Select(t => t.BlobName)
            .ToListAsync(cancellationToken);

        await using var uploadStream = new MemoryStream(topo.Bytes);
        var blobName = await mediaUpload.UploadNumberedAsync(blobPrefix, extension, usedBlobNames, uploadStream, topo.ContentType);
        if (blobName is null) return false;

        db.Topos.Add(new Topo
        {
            RouteId = route.Id,
            BlobName = blobName,
            ContentType = topo.ContentType,
            FileName = topo.FileName,
            // The actual submitter — cleaner than enrichment, which had to attribute to
            // route.AddedByUserId because no human was in the loop.
            UploadedByUserId = import.SubmittedByUserId
        });

        var stagedBlobName = import.BlobName;
        if (stagedBlobName is not null) import.BlobName = blobName;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            import.BlobName = stagedBlobName;
            await blobStorage.DeleteAsync(blobName);
            throw;
        }

        if (stagedBlobName is not null) await blobStorage.DeleteAsync(stagedBlobName);
        logger.LogInformation("route import attached topo {Blob} to route {RouteId}", blobName, route.Id);
        return true;
    }

    private static string ExtensionFor(RouteImport import, TopoCandidate topo)
    {
        if (import.BlobName is { } staged && Path.GetExtension(staged) is { Length: > 0 } stagedExtension)
        {
            return stagedExtension;
        }
        var fromName = Path.GetExtension(topo.FileName);
        return fromName.Length > 0 ? fromName : ".jpg";
    }

    // On a discard an Image import's staged blob goes immediately but the row survives with
    // its reason: the image is the thing that costs money to keep, the explanation is what
    // the user needs. A Discarded Image row's retry in the frontend re-uploads rather than
    // re-enqueueing, precisely because the blob is gone by then; a Url row genuinely can be
    // resubmitted, because the artifact is the URL still on the row.
    private async Task DiscardAsync(RouteImport import, string outcome, CancellationToken cancellationToken)
    {
        if (import.BlobName is { } blobName) await blobStorage.DeleteAsync(blobName);
        await FinishAsync(import, RouteImportStatus.Discarded, outcome, cancellationToken);
    }

    private async Task FinishAsync(RouteImport import, RouteImportStatus status, string outcome, CancellationToken cancellationToken)
    {
        import.Status = status;
        import.Outcome = outcome;
        import.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private void LogStages(RouteImport import, string stage, bool extracted, bool matched, bool created) =>
        logger.LogInformation(
            "route import stage={Stage} kind={Kind} import={ImportId} extracted={Extracted} matched={Matched} created={Created}",
            stage, import.Kind, import.Id, extracted, matched, created);
}
