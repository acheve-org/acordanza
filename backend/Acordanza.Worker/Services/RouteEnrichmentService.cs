using Acordanza.Core.Services;
using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Worker.Services;

// Does the enrichment work for one route: calls the provider, writes found links
// (via LinkRules, hoisted so the human-entry and worker paths share the same
// allowlist), and verifies+imports a topo candidate. Writes via AppDbContext
// directly, never through RoutesController.UpdateRoute, so Validation.InvalidateIfChanged
// is structurally out of the path — auto-appended links must never bounce an
// approved route back to Pending.
public class RouteEnrichmentService(
    AppDbContext db,
    AreaHierarchyService areaHierarchy,
    IRouteEnrichmentProvider provider,
    ITopoVerificationProvider topoVerification,
    MediaUploadService mediaUpload,
    BlobStorageService blobStorage,
    IHttpClientFactory httpClientFactory,
    ILogger<RouteEnrichmentService> logger)
{
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(10);
    private const long MaxDownloadBytes = 10 * 1024 * 1024;

    // A cheap HEAD-shaped GET per surviving candidate, on a path that already spends
    // dollars — a grounded URL can still be dead, paywalled, or a redirect to a homepage.
    private static readonly TimeSpan LinkCheckTimeout = TimeSpan.FromSeconds(5);

    // Both caps live here rather than in LinkRules: that is the *human-entry* contract
    // shared with RoutesController, and tightening it would silently constrain the edit
    // form too. The useful outcome for an obscure sport route is one good blog post, not a
    // page of results — hence one link per host, four per run, on top of LinkRules.MaxLinks.
    private const int MaxAppendedPerRun = 4;

    // topo first (a link to the page publishing a topo is the single most useful thing this
    // can return), then descending usefulness. Anything unrecognised sorts last.
    private static int KindRank(string? kind) => kind switch
    {
        "topo" => 0,
        "guidebook" => 1,
        "first-ascent" => 2,
        "video" => 3,
        "sector" => 4,
        _ => 5
    };

    public async Task EnrichAsync(int routeId, bool force, CancellationToken cancellationToken = default)
    {
        var route = await db.Routes
            .Include(r => r.Links)
            .Include(r => r.Topos)
            .Include(r => r.FirstAscensionists)
            .FirstOrDefaultAsync(r => r.Id == routeId, cancellationToken);
        if (route is null)
        {
            logger.LogWarning("enrichment skipped: route {RouteId} not found", routeId);
            return;
        }

        if (route.EnrichedAt is not null && !force)
        {
            // Idempotency: a redelivered/duplicate message finds this already done.
            logger.LogInformation("enrichment skipped: route {RouteId} already enriched at {EnrichedAt}", routeId, route.EnrichedAt);
            return;
        }

        var areas = await areaHierarchy.GetAsync();
        // Not on the AreaHierarchy snapshot, which is deliberately id/name/slug/parent only
        // and shared with the API — one extra projection is cheaper than widening it for a
        // single worker consumer.
        var centroid = await db.Areas.AsNoTracking()
            .Where(a => a.Id == route.AreaId)
            .Select(a => new { a.CentroidLat, a.CentroidLng })
            .FirstOrDefaultAsync(cancellationToken);

        var context = new RouteEnrichmentContext(
            route.Name,
            route.Grade,
            areas.NamePath(route.AreaId),
            route.FirstAscensionists.Select(fa => fa.Name).ToList(),
            route.Length,
            route.Orientation,
            route.Gear,
            centroid?.CentroidLat,
            centroid?.CentroidLng);
        logger.LogInformation("enrichment calling provider {Provider} for route {RouteId} ({RouteName})",
            provider.GetType().Name, routeId, route.Name);
        var result = await provider.FindAsync(context, cancellationToken);
        logger.LogInformation(
            "enrichment provider returned {LinkCount} link(s), topoPageUrl={HasTopoPage}, topoImageUrl={HasTopo}, completed={Completed} for route {RouteId}",
            result.Links.Count, result.TopoPageUrl is not null, result.TopoImageUrl is not null, result.Completed, routeId);

        // The topo runs first because its outcome decides whether topoPageUrl becomes a
        // link: an imported image makes the page redundant, a missing or rejected one makes
        // the page the best available answer.
        var topoImported = false;
        if (result.TopoImageUrl is not null && route.Topos.Count == 0)
        {
            topoImported = await TryImportTopoAsync(route, areas.SlugPath(route.AreaId), result.TopoImageUrl, context, cancellationToken);
        }

        var existingUrls = route.Links.Select(l => l.Url).ToHashSet();
        var valid = SelectCandidates(result, existingUrls, topoImported);

        var reachable = new List<EnrichmentLink>();
        foreach (var candidate in valid)
        {
            if (await IsReachableHtmlPageAsync(candidate.Url, cancellationToken))
            {
                reachable.Add(candidate);
            }
        }

        var toAppend = ApplyCaps(reachable, route.Links.Count);
        route.Links.AddRange(toAppend.Select(l => new Link { Url = l.Url, Source = LinkSource.Enrichment }));

        // Only a run that produced a well-formed answer counts as done. Marking a route
        // enriched after a pause_turn or a Foundry timeout would permanently need force=true
        // to retry something that only failed for an environmental reason.
        if (result.Completed)
        {
            route.EnrichedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);

        // The whole decision chain as one line: which stage did the filtering is the
        // difference between tuning the prompt and tuning the filter (plan 29 §7).
        logger.LogInformation(
            "enrichment stages route={RouteId} grounded={Grounded} valid={Valid} reachable={Reachable} appended={Appended} topoImported={TopoImported} enrichedAtSet={EnrichedAtSet}",
            routeId, result.Links.Count, valid.Count, reachable.Count, toAppend.Count,
            topoImported, result.Completed);
    }

    // The pure half of the pipeline — everything between "what the provider stood behind"
    // and "what is worth spending a reachability request on". Split out so the golden set
    // (EnrichmentGoldenSetTests) measures the same selection production applies rather than
    // a second implementation of it that could drift.
    internal static List<EnrichmentLink> SelectCandidates(
        EnrichmentResult result, ISet<string> existingUrls, bool topoImported)
    {
        // Confidence and evidence are the model's own admission of doubt, and this is where
        // it costs it something: anything but a high-confidence claim carrying a real quote
        // is discarded outright rather than appended with a caveat nobody would ever see.
        var candidates = result.Links
            .Where(l => string.Equals(l.Confidence, "high", StringComparison.OrdinalIgnoreCase))
            .Where(l => !string.IsNullOrWhiteSpace(l.Evidence))
            .ToList();

        if (!topoImported && result.TopoPageUrl is not null)
        {
            // Plan 18 already anticipated this for the rejected-image case; plan 29 makes it
            // the primary fallback. A link to the page that publishes the topo is a
            // genuinely good outcome — and, unlike importing the scan, carries no copyright
            // exposure at all.
            candidates.Add(new EnrichmentLink(result.TopoPageUrl, null, "topo", null, "high"));
        }

        // LinkRules.Resolve validates a whole batch at once (all-or-nothing — right for
        // the human-entry form, where one bad link should surface as one error), so a
        // single junk URL from the model can't be allowed to reject every link it found.
        // Resolve one at a time instead: the allowlist/length rules still come from
        // LinkRules (never re-derived here), but a single miss just drops that one URL.
        return candidates
            .OrderBy(l => KindRank(l.Kind))
            .Select(l => (Link: l, Urls: LinkRules.Resolve([l.Url]).Urls))
            .Where(x => x.Urls is { Count: > 0 })
            .Select(x => x.Link with { Url = x.Urls![0] })
            .Where(l => !existingUrls.Contains(l.Url))
            .GroupBy(l => l.Url)
            .Select(g => g.First())
            .ToList();
    }

    internal static List<EnrichmentLink> ApplyCaps(IEnumerable<EnrichmentLink> candidates, int existingLinkCount)
    {
        var headroom = Math.Min(MaxAppendedPerRun, Math.Max(0, LinkRules.MaxLinks - existingLinkCount));
        var seenHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<EnrichmentLink>();
        foreach (var candidate in candidates)
        {
            if (kept.Count >= headroom) break;
            if (!seenHosts.Add(HostOf(candidate.Url))) continue;
            kept.Add(candidate);
        }
        return kept;
    }

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : url;

    // Same shape as TryImportTopoAsync's download — headers only, short timeout, response
    // disposed before the body is read — so this is a lift of an existing pattern rather
    // than a second HTTP idiom in the worker.
    private async Task<bool> IsReachableHtmlPageAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = LinkCheckTimeout;

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("enrichment link dropped: unreachable ({Status}) {Url}", response.StatusCode, url);
                return false;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!string.Equals(contentType, "text/html", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("enrichment link dropped: not a page ({ContentType}) {Url}", contentType, url);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogInformation(ex, "enrichment link dropped: request error for {Url}", url);
            return false;
        }
    }

    private async Task<bool> TryImportTopoAsync(
        ClimbingRoute route, string areaSlugPath, string topoImageUrl, RouteEnrichmentContext context, CancellationToken cancellationToken)
    {
        byte[] bytes;
        string contentType;
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = DownloadTimeout;

            using var response = await client.GetAsync(topoImageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("enrichment topo skipped: download failed ({Status}) for {Url}", response.StatusCode, topoImageUrl);
                return false;
            }

            contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!MediaUploadService.AllowedImageContentTypes.Contains(contentType))
            {
                logger.LogInformation("enrichment topo skipped: unsupported content type {ContentType} for {Url}", contentType, topoImageUrl);
                return false;
            }

            if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
            {
                logger.LogInformation("enrichment topo skipped: too large for {Url}", topoImageUrl);
                return false;
            }

            await using var stream = new MemoryStream();
            await response.Content.CopyToAsync(stream, cancellationToken);
            if (stream.Length > MaxDownloadBytes)
            {
                logger.LogInformation("enrichment topo skipped: too large for {Url}", topoImageUrl);
                return false;
            }

            bytes = stream.ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogInformation(ex, "enrichment topo skipped: download error for {Url}", topoImageUrl);
            return false;
        }

        var isTopo = await topoVerification.IsTopoAsync(
            new TopoVerificationContext(context.Name, context.Grade, context.AreaBreadcrumb, bytes, contentType),
            cancellationToken);
        if (!isTopo)
        {
            // The page it came from is still a fine link even when the image itself
            // doesn't clear the bar — returning false here is what lets the caller fall
            // back to appending result.TopoPageUrl.
            logger.LogInformation("enrichment topo skipped: verification rejected image for route {RouteId} ({Url})", route.Id, topoImageUrl);
            return false;
        }

        var extension = contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ""
        };

        var blobPrefix = $"{areaSlugPath}/{Slugify.ToSlug(route.Name)}-";
        var usedBlobNames = await db.Topos.AsNoTracking()
            .Where(t => t.BlobName.StartsWith(blobPrefix))
            .Select(t => t.BlobName)
            .ToListAsync(cancellationToken);

        await using var uploadStream = new MemoryStream(bytes);
        var blobName = await mediaUpload.UploadNumberedAsync(blobPrefix, extension, usedBlobNames, uploadStream, contentType);
        if (blobName is null)
        {
            logger.LogWarning("enrichment topo skipped: no free blob name for route {RouteId}", route.Id);
            return false;
        }

        var topo = new Topo
        {
            RouteId = route.Id,
            BlobName = blobName,
            ContentType = contentType,
            FileName = $"topo{extension}",
            // The enrichment runs on the route creator's behalf — Topo has no
            // nullable uploader, and there's no system-user hack here.
            UploadedByUserId = route.AddedByUserId
        };
        db.Topos.Add(topo);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await blobStorage.DeleteAsync(blobName);
            throw;
        }

        logger.LogInformation("enrichment topo imported for route {RouteId}: {BlobName}", route.Id, blobName);
        return true;
    }
}
