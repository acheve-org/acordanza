using Acordanza.Core.Models;
using Acordanza.Core.Services;

namespace Acordanza.Worker.Services;

// The Url kind (plan 31 §3–§5): fetch the page ourselves, strip it to text, read the route
// off it in one call, then sweep the page's images for the topo.
//
// The sweep runs *after* the extraction rather than in parallel with it, because the
// question ITopoVerificationProvider answers — "is this a topo on which route X's line or
// name is identifiably marked" — needs the route name and grade the extraction produces.
// That is also why this kind needs no new vision provider at all: it reuses enrichment's.
public class WebPageReader(
    AreaHierarchyService areaHierarchy,
    PageFetcher fetcher,
    IPageExtractionProvider extraction,
    ITopoVerificationProvider topoVerification,
    ILogger<WebPageReader> logger) : IImportReader
{
    // Anything smaller than this got past the path filter and is furniture: a spacer, a
    // favicon-sized decoration, a badge nobody named "badge".
    private const int MinimumTopoBytes = 20 * 1024;

    public RouteImportKind Kind => RouteImportKind.Url;

    public async Task<RouteReading> ReadAsync(RouteImport import, CancellationToken cancellationToken)
    {
        var url = import.SourceUrl!;

        var page = await fetcher.FetchPageAsync(url, cancellationToken);
        if (page is null)
        {
            // A blocked address, a redirect loop, a 404, a PDF, a YouTube link, a body over
            // the cap: all of them are "we did not get an HTML page from this URL", which
            // is a real answer to the submitter, not a pipeline failure to retry.
            logger.LogInformation("route import {ImportId}: could not fetch {Url}", import.Id, url);
            return RouteReading.Discard(RouteImportOutcomes.PageUnreachable);
        }

        var text = PageText.Extract(page.Body);
        if (text.Length < PageText.MinimumUsefulCharacters)
        {
            // The JS-rendered/paywalled case. Better to say so than to send the model an
            // empty page and let it hallucinate into the gap.
            logger.LogInformation("route import {ImportId}: {Url} stripped to {Length} characters", import.Id, url, text.Length);
            return RouteReading.Discard(RouteImportOutcomes.NoReadableText);
        }

        var areas = await areaHierarchy.GetAsync();
        var breadcrumb = areas.NamePath(import.AreaId);
        var extracted = await extraction.ExtractAsync(
            new PageExtractionContext(page.Uri.ToString(), text, breadcrumb), cancellationToken);

        if (!extracted.Completed)
        {
            logger.LogWarning("route import {ImportId}: page extraction did not complete", import.Id);
            return RouteReading.Broke(RouteImportOutcomes.Failed);
        }

        var (candidate, discardReason) = Accept(extracted);
        if (candidate is null)
        {
            logger.LogInformation("route import {ImportId}: page extraction discarded ({Reason})", import.Id, extracted.Reason);
            return RouteReading.Discard(discardReason!);
        }

        var topo = await SweepForTopoAsync(page, candidate, breadcrumb, cancellationToken);

        logger.LogInformation(
            "route import kind=url import={ImportId} fetch=ok text={TextLength} extracted=true topoFound={TopoFound}",
            import.Id, text.Length, topo is not null);

        return new RouteReading(candidate, topo, null);
    }

    // The pure half: everything between "what the model returned" and "what we're willing
    // to write". A static, like TopoImageReader.Accept, so the unit tests and the golden
    // set measure the same predicate production applies.
    internal static (RouteCandidate? Candidate, string? DiscardReason) Accept(PageExtractionResult result)
    {
        if (!result.IsClimbingRoute)
        {
            return (null, RouteImportOutcomes.NotAboutARoute);
        }

        // A round-up naming six routes equally is a real answer, and a confidently wrong
        // single route is worse for the reviewer than a stated failure. Checked before the
        // route itself, since the model may fill both.
        if (result.MultipleRoutes && result.Route is null)
        {
            return (null, RouteImportOutcomes.MultipleRoutesOnPage);
        }

        if (result.Route is not { } route || string.IsNullOrWhiteSpace(route.Name))
        {
            return (null, RouteImportOutcomes.NoRouteNameOnPage);
        }

        if (!string.Equals(route.Confidence, "high", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(route.Evidence))
        {
            return (null, RouteImportOutcomes.LowConfidence);
        }

        if (!GradeValidator.IsValid(route.Grade))
        {
            return (null, RouteImportOutcomes.UnreadableGradeOnPage);
        }

        var openers = (route.FirstAscensionists ?? [])
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (new RouteCandidate(
            route.Name.Trim(),
            GradeValidator.Normalize(route.Grade),
            RouteImportService.AcceptableLength(route.Length),
            route.Evidence!.Trim(),
            (route.Description ?? "").Trim(),
            openers,
            // A cross-check, not an input (plan 31 §2): the spine turns a mismatch into a
            // note in Outcome and nothing else, because crag naming is inconsistent enough
            // that a false discard here would be both infuriating and frequent.
            string.IsNullOrWhiteSpace(result.SectorMentioned) ? null : result.SectorMentioned.Trim()), null);
    }

    // Candidates in order, first hit wins, the rest never downloaded. Worst case is eight
    // Haiku calls at ~$0.002; the typical case is one or two, which is what the ordering in
    // PageImages buys.
    private async Task<TopoCandidate?> SweepForTopoAsync(
        FetchedPage page, RouteCandidate candidate, string breadcrumb, CancellationToken cancellationToken)
    {
        var candidates = PageImages.Extract(page.Body, page.Uri);
        logger.LogInformation("route import image sweep: {Count} candidate(s) on {Url}", candidates.Count, page.Uri);

        foreach (var imageUrl in candidates)
        {
            var image = await fetcher.FetchImageAsync(imageUrl, cancellationToken);
            if (image is null) continue;
            if (!MediaUploadService.AllowedImageContentTypes.Contains(image.ContentType)) continue;
            if (image.Bytes.Length < MinimumTopoBytes) continue;

            var verified = await topoVerification.IsTopoAsync(
                new TopoVerificationContext(candidate.Name, candidate.Grade, breadcrumb, image.Bytes, image.ContentType),
                cancellationToken);
            if (!verified) continue;

            logger.LogInformation("route import image sweep: {Url} verified as topo", imageUrl);
            return new TopoCandidate(image.Bytes, image.ContentType, FileNameFor(imageUrl, image.ContentType));
        }

        return null;
    }

    // Topo.FileName is what the frontend shows under a photo, so it wants to look like a
    // file: the last path segment when the URL has one, the route otherwise.
    private static string FileNameFor(Uri url, string contentType)
    {
        var segment = url.Segments.LastOrDefault()?.Trim('/');
        if (!string.IsNullOrWhiteSpace(segment) && Path.HasExtension(segment)) return segment;

        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
        return $"topo{extension}";
    }
}
