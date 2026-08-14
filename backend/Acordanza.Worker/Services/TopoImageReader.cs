using Acordanza.Core.Models;
using Acordanza.Core.Services;

namespace Acordanza.Worker.Services;

// The Image kind (plan 30 §3), behind plan 31's reader seam: download the staged photo,
// ask a cheap model whether it is a topo at all, then read the route off it.
//
// Two calls rather than one, and the gate earns its round trip because the expensive
// thing here is *resolution*: Haiku downsamples to a 1568 px long edge, Opus 5 to
// 2576 px, so a binary pre-filter genuinely avoids a costlier call. (The Url kind makes
// the opposite call for the opposite reason — see WebPageReader.)
public class TopoImageReader(
    AreaHierarchyService areaHierarchy,
    ITopoImportGateProvider gate,
    ITopoExtractionProvider extraction,
    BlobStorageService blobStorage,
    ILogger<TopoImageReader> logger) : IImportReader
{
    public RouteImportKind Kind => RouteImportKind.Image;

    public async Task<RouteReading> ReadAsync(RouteImport import, CancellationToken cancellationToken)
    {
        var blob = await blobStorage.DownloadAsync(import.BlobName!);
        if (blob is null)
        {
            // The staged blob is gone — most likely swept by the _imports/ lifecycle rule
            // after a run that sat undelivered for a week. Failed, not Discarded: we never
            // looked at the image.
            logger.LogWarning("route import {ImportId}: staged blob {Blob} missing", import.Id, import.BlobName);
            return RouteReading.Broke(RouteImportOutcomes.ImageGone);
        }

        var (bytes, storedContentType) = blob.Value;
        // The row's ContentType came through ImageValidation on upload; the blob's own
        // header is only a fallback for the (never expected) case it wasn't preserved.
        var contentType = import.ContentType is { } declared && MediaUploadService.AllowedImageContentTypes.Contains(declared)
            ? declared
            : storedContentType;

        var gateResult = await gate.IsTopoAsync(new TopoImportGateContext(bytes, contentType), cancellationToken);
        if (!gateResult.Completed)
        {
            logger.LogWarning("route import {ImportId}: gate did not complete", import.Id);
            return RouteReading.Broke(RouteImportOutcomes.Failed);
        }

        if (!gateResult.IsTopo)
        {
            logger.LogInformation("route import {ImportId}: gate rejected ({Reason})", import.Id, gateResult.Reason);
            return RouteReading.Discard(RouteImportOutcomes.NotATopo);
        }

        var areas = await areaHierarchy.GetAsync();
        var extracted = await extraction.ExtractAsync(
            new TopoExtractionContext(bytes, contentType, areas.NamePath(import.AreaId)), cancellationToken);

        if (!extracted.Completed)
        {
            logger.LogWarning("route import {ImportId}: extraction did not complete", import.Id);
            return RouteReading.Broke(RouteImportOutcomes.Failed);
        }

        var (candidate, discardReason) = Accept(extracted);
        if (candidate is null)
        {
            logger.LogInformation("route import {ImportId}: extraction discarded ({Reason})", import.Id, extracted.Reason);
            return RouteReading.Discard(discardReason!);
        }

        // The uploaded photo *is* the topo — it passed the gate, and the extraction read
        // this route off it. No second verification call: ITopoVerificationProvider asks
        // "is route X marked on this image", which the gate plus the extraction have
        // between them already answered.
        var topo = new TopoCandidate(bytes, contentType, import.FileName ?? "topo");
        return new RouteReading(candidate, topo, null);
    }

    // The pure half of the Image kind: everything between "what the model returned" and
    // "what we're willing to write". A static so the unit tests (and the golden set)
    // measure the same predicate production applies rather than a second implementation
    // of it that could drift.
    internal static (RouteCandidate? Candidate, string? DiscardReason) Accept(TopoExtractionResult result)
    {
        // A topo showing several routes with no dominant one is a discard, not a coin
        // flip: a confidently wrong route is worse than a stated failure the uploader can
        // act on. Checked before the route itself, since the model may fill both.
        if (result.MultipleRoutes && result.Route is null)
        {
            return (null, RouteImportOutcomes.MultipleRoutes);
        }

        if (result.Route is not { } route || string.IsNullOrWhiteSpace(route.Name))
        {
            return (null, RouteImportOutcomes.NoRouteName);
        }

        // Confidence and evidence are the model's own admission of doubt, and this is
        // where it costs it something — same bar RouteEnrichmentService.SelectCandidates
        // sets on a link.
        if (!string.Equals(route.Confidence, "high", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(route.Evidence))
        {
            return (null, RouteImportOutcomes.LowConfidence);
        }

        // The model is told to copy the grade and never to convert it, so an
        // unconvertible grade is a discard with a reason, not a best effort.
        if (!GradeValidator.IsValid(route.Grade))
        {
            return (null, RouteImportOutcomes.UnreadableGrade);
        }

        return (new RouteCandidate(
            route.Name.Trim(),
            GradeValidator.Normalize(route.Grade),
            RouteImportService.AcceptableLength(route.Length),
            route.Evidence!.Trim()), null);
    }
}
