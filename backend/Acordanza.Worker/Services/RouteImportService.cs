using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Acordanza.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Worker.Services;

// Every user-facing string this pipeline can write. Spanish, like every { error } payload
// the frontend surfaces — TopoImport.Outcome is read by the person who uploaded the photo.
// Gathered here rather than inlined so the tests assert against the same constants
// production writes, and so the whole vocabulary of "why did this not work" is one screen.
public static class TopoImportOutcomes
{
    public const string NotATopo = "No parece un topo de escalada";
    public const string NoRouteName = "El topo no indica el nombre de la vía";
    public const string MultipleRoutes = "El topo muestra varias vías y ninguna es claramente la principal";
    public const string UnreadableGrade = "No se pudo leer un grado válido (escala francesa)";
    public const string LowConfidence = "No se pudo leer el topo con suficiente claridad";
    public const string Failed = "No se pudo leer el topo, inténtalo de nuevo";
    public const string ImageGone = "La imagen ya no está disponible, vuelve a subirla";
    public const string RouteCreated = "Vía creada y pendiente de validación";
    public const string RouteMatched = "La vía ya existía; se ha añadido el topo";
}

// Does the topo-import work for one upload: gate → extract → match-or-create → promote
// the staged blob (plan 30 §3–§4).
//
// Writes through AppDbContext directly, never through RoutesController.CreateRoute, so
// Validation.MarkValidated is structurally out of the path. This matters: CreateRoute
// auto-approves when the creator can validate the area, and an admin photographing a topo
// must still get a route somebody has *read* before it goes live. The worker never approves.
public class RouteImportService(
    AppDbContext db,
    AreaHierarchyService areaHierarchy,
    ITopoImportGateProvider gate,
    ITopoExtractionProvider extraction,
    MediaUploadService mediaUpload,
    BlobStorageService blobStorage,
    ILogger<RouteImportService> logger)
{
    public async Task ImportAsync(int importId, CancellationToken cancellationToken = default)
    {
        var import = await db.TopoImports.FirstOrDefaultAsync(i => i.Id == importId, cancellationToken);
        if (import is null)
        {
            logger.LogWarning("topo import skipped: import {ImportId} not found", importId);
            return;
        }

        if (import.Status != TopoImportStatus.Pending)
        {
            // Idempotency: a redelivered/duplicate message finds this already decided.
            logger.LogInformation("topo import skipped: import {ImportId} already {Status}", importId, import.Status);
            return;
        }

        var blob = await blobStorage.DownloadAsync(import.BlobName);
        if (blob is null)
        {
            // The staged blob is gone — most likely swept by the _imports/ lifecycle rule
            // after a run that sat undelivered for a week. Failed, not Discarded: we never
            // looked at the image.
            logger.LogWarning("topo import {ImportId}: staged blob {Blob} missing", importId, import.BlobName);
            await FinishAsync(import, TopoImportStatus.Failed, TopoImportOutcomes.ImageGone, cancellationToken);
            return;
        }

        var (bytes, storedContentType) = blob.Value;
        // The row's ContentType came through ImageValidation on upload; the blob's own
        // header is only a fallback for the (never expected) case it wasn't preserved.
        var contentType = MediaUploadService.AllowedImageContentTypes.Contains(import.ContentType)
            ? import.ContentType
            : storedContentType;

        var gateResult = await gate.IsTopoAsync(new TopoImportGateContext(bytes, contentType), cancellationToken);
        if (!gateResult.Completed)
        {
            logger.LogWarning("topo import {ImportId}: gate did not complete", importId);
            await FinishAsync(import, TopoImportStatus.Failed, TopoImportOutcomes.Failed, cancellationToken);
            LogStages(importId, "gate-failed", gate: null, extracted: false, matched: false, created: false);
            return;
        }

        if (!gateResult.IsTopo)
        {
            logger.LogInformation("topo import {ImportId}: gate rejected ({Reason})", importId, gateResult.Reason);
            await DiscardAsync(import, TopoImportOutcomes.NotATopo, cancellationToken);
            LogStages(importId, "gate-rejected", gate: false, extracted: false, matched: false, created: false);
            return;
        }

        var areas = await areaHierarchy.GetAsync();
        var extracted = await extraction.ExtractAsync(
            new TopoExtractionContext(bytes, contentType, areas.NamePath(import.AreaId)), cancellationToken);

        if (!extracted.Completed)
        {
            logger.LogWarning("topo import {ImportId}: extraction did not complete", importId);
            await FinishAsync(import, TopoImportStatus.Failed, TopoImportOutcomes.Failed, cancellationToken);
            LogStages(importId, "extraction-failed", gate: true, extracted: false, matched: false, created: false);
            return;
        }

        var (candidate, discardReason) = Accept(extracted);
        if (candidate is null)
        {
            logger.LogInformation("topo import {ImportId}: extraction discarded ({Reason})", importId, extracted.Reason);
            await DiscardAsync(import, discardReason!, cancellationToken);
            LogStages(importId, "extraction-discarded", gate: true, extracted: false, matched: false, created: false);
            return;
        }

        // Matching is deterministic and needs no fuzzy logic, because the sector was
        // given: Slug is already unique per AreaId and regenerated whenever the name or
        // area changes, so it is precisely the right key. Scoped to the given area
        // *exactly*, not its descendants — the uploader picked that sector explicitly.
        var candidateSlug = Slugify.ToSlug(candidate.Name);
        var existing = await db.Routes
            .Include(r => r.Topos)
            .FirstOrDefaultAsync(r => r.AreaId == import.AreaId && r.Slug == candidateSlug, cancellationToken);

        var route = existing ?? await CreateRouteAsync(import, candidate, cancellationToken);
        var created = existing is null;

        var topoImported = await PromoteTopoAsync(import, route, areas.SlugPath(route.AreaId), bytes, contentType, cancellationToken);
        if (!topoImported)
        {
            // Nothing free to name the blob, or the upload lost every race. The route (if
            // it was created) still stands — it's the useful half — but the row has to say
            // the photo didn't land.
            logger.LogWarning("topo import {ImportId}: topo promotion failed for route {RouteId}", importId, route.Id);
        }

        import.RouteId = route.Id;
        import.RouteWasCreated = created;
        import.Outcome = created
            ? TopoImportOutcomes.RouteCreated
            : MatchOutcome(candidate, route);
        await FinishAsync(import, TopoImportStatus.Completed, import.Outcome, cancellationToken);

        // The whole decision chain as one line — the direct analogue of plan 29 §7's stage
        // counts, and the thing that says whether to tune the gate prompt or the
        // extraction prompt.
        LogStages(importId, "completed", gate: true, extracted: true, matched: !created, created: created);
    }

    // The pure half of the pipeline: everything between "what the model returned" and
    // "what we're willing to write". Split out as a static so the unit tests (and the
    // golden set) measure the same predicate production applies rather than a second
    // implementation of it that could drift.
    internal static (RouteCandidate? Candidate, string? DiscardReason) Accept(TopoExtractionResult result)
    {
        // A topo showing several routes with no dominant one is a discard, not a coin
        // flip: a confidently wrong route is worse than a stated failure the uploader can
        // act on. Checked before the route itself, since the model may fill both.
        if (result.MultipleRoutes && result.Route is null)
        {
            return (null, TopoImportOutcomes.MultipleRoutes);
        }

        if (result.Route is not { } route || string.IsNullOrWhiteSpace(route.Name))
        {
            return (null, TopoImportOutcomes.NoRouteName);
        }

        // Confidence and evidence are the model's own admission of doubt, and this is
        // where it costs it something — same bar RouteEnrichmentService.SelectCandidates
        // sets on a link.
        if (!string.Equals(route.Confidence, "high", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(route.Evidence))
        {
            return (null, TopoImportOutcomes.LowConfidence);
        }

        // The model is told to copy the grade and never to convert it, so an
        // unconvertible grade is a discard with a reason, not a best effort.
        if (!GradeValidator.IsValid(route.Grade))
        {
            return (null, TopoImportOutcomes.UnreadableGrade);
        }

        return (new RouteCandidate(
            route.Name.Trim(),
            GradeValidator.Normalize(route.Grade),
            AcceptableLength(route.Length),
            route.Evidence!.Trim()), null);
    }

    // ClimbingRoute.Length is a non-nullable int with no "unknown" value, so a topo that
    // doesn't state a length leaves the column default of 0. Note that 0 is *below*
    // RoutesController.ValidateLength's floor (> 5): a reviewer opening such a route in
    // the edit form has to supply a length before they can save it. That is the intended
    // behaviour — the topo didn't say, so a human must — but it is why the extraction
    // prompt asks for null rather than an estimate, and why an out-of-range value the
    // model did return is dropped here rather than written through.
    private static int AcceptableLength(int? length) => length is > 5 and < 3000 ? length.Value : 0;

    private async Task<ClimbingRoute> CreateRouteAsync(
        TopoImport import, RouteCandidate candidate, CancellationToken cancellationToken)
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
            // The uploader, so the route lands in their /my-acordanza/contributions and
            // stays visible to them under plan 11's rule while it waits for review.
            AddedByUserId = import.UploadedByUserId,
            // Left at Pending explicitly, including when the uploader is an admin. This is
            // the invariant the integration tests assert: CreateRoute does the opposite,
            // and the whole reason this write bypasses it.
            ValidationStatus = ReviewStatus.Pending
        };

        db.Routes.Add(route);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "topo import created route {RouteId} '{Name}' ({Grade}) in area {AreaId}",
            route.Id, route.Name, route.Grade, route.AreaId);
        return route;
    }

    // A grade disagreement does not block the match and does not rewrite the route:
    // silently correcting a human-entered grade from a photograph is the wrong default in
    // a catalogue whose whole point is that people entered it. Record it and let a human
    // decide. ValidationStatus is likewise untouched — adding a photo is not an edit of
    // the route's data, and UploadTopo doesn't invalidate a route today either.
    private static string MatchOutcome(RouteCandidate candidate, ClimbingRoute route) =>
        string.Equals(candidate.Grade, route.Grade, StringComparison.OrdinalIgnoreCase)
            ? TopoImportOutcomes.RouteMatched
            : $"La vía ya existía; el topo indica {candidate.Grade} y la ficha {route.Grade}";

    // Blob Storage has no rename, so promotion is really re-upload + delete: the staged
    // bytes are already in memory (they fed the vision calls), so this writes them under
    // the route's own prefix exactly as a human UploadTopo would, then drops the staged
    // copy and repoints the row at the promoted name.
    private async Task<bool> PromoteTopoAsync(
        TopoImport import, ClimbingRoute route, string areaSlugPath, byte[] bytes, string contentType,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(import.BlobName);
        var blobPrefix = $"{areaSlugPath}/{Slugify.ToSlug(route.Name)}-";
        var usedBlobNames = await db.Topos.AsNoTracking()
            .Where(t => t.BlobName.StartsWith(blobPrefix))
            .Select(t => t.BlobName)
            .ToListAsync(cancellationToken);

        await using var uploadStream = new MemoryStream(bytes);
        var blobName = await mediaUpload.UploadNumberedAsync(blobPrefix, extension, usedBlobNames, uploadStream, contentType);
        if (blobName is null) return false;

        var topo = new Topo
        {
            RouteId = route.Id,
            BlobName = blobName,
            ContentType = contentType,
            FileName = import.FileName,
            // The actual uploader — cleaner than enrichment, which had to attribute to
            // route.AddedByUserId because no human was in the loop.
            UploadedByUserId = import.UploadedByUserId
        };
        db.Topos.Add(topo);

        var stagedBlobName = import.BlobName;
        import.BlobName = blobName;
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

        await blobStorage.DeleteAsync(stagedBlobName);
        logger.LogInformation("topo import promoted {Staged} to {Promoted} for route {RouteId}", stagedBlobName, blobName, route.Id);
        return true;
    }

    // On a discard the staged blob goes immediately but the row survives with its reason:
    // the image is the thing that costs money to keep, the explanation is what the user
    // needs. A Discarded row's retry in the frontend re-uploads rather than re-enqueueing,
    // precisely because the blob is gone by then.
    private async Task DiscardAsync(TopoImport import, string outcome, CancellationToken cancellationToken)
    {
        await blobStorage.DeleteAsync(import.BlobName);
        await FinishAsync(import, TopoImportStatus.Discarded, outcome, cancellationToken);
    }

    private async Task FinishAsync(TopoImport import, TopoImportStatus status, string outcome, CancellationToken cancellationToken)
    {
        import.Status = status;
        import.Outcome = outcome;
        import.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private void LogStages(int importId, string stage, bool? gate, bool extracted, bool matched, bool created) =>
        logger.LogInformation(
            "topo import stage={Stage} import={ImportId} gate={Gate} extracted={Extracted} matched={Matched} created={Created}",
            stage, importId, gate, extracted, matched, created);
}

// What the pipeline is willing to write, after Accept: every field non-null and already
// normalised, so nothing downstream re-checks them.
public record RouteCandidate(string Name, string Grade, int Length, string Evidence);
