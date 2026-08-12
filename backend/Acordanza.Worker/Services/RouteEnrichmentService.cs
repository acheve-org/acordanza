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

    public async Task EnrichAsync(int routeId, bool force, CancellationToken cancellationToken = default)
    {
        var route = await db.Routes
            .Include(r => r.Links)
            .Include(r => r.Topos)
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
        var context = new RouteEnrichmentContext(route.Name, route.Grade, areas.NamePath(route.AreaId));
        logger.LogInformation("enrichment calling provider {Provider} for route {RouteId} ({RouteName})",
            provider.GetType().Name, routeId, route.Name);
        var result = await provider.FindAsync(context, cancellationToken);
        logger.LogInformation("enrichment provider returned {LinkCount} link(s), topoImageUrl={HasTopo} for route {RouteId}",
            result.Links.Count, result.TopoImageUrl is not null, routeId);

        // LinkRules.Resolve validates a whole batch at once (all-or-nothing — right for
        // the human-entry form, where one bad link should surface as one error), so a
        // single junk URL from the model can't be allowed to reject every link it found.
        // Resolve one at a time instead: the allowlist/length rules still come from
        // LinkRules (never re-derived here), but a single miss just drops that one URL.
        var validLinks = result.Links
            .Select(link => LinkRules.Resolve([link]).Urls)
            .Where(urls => urls is { Count: > 0 })
            .Select(urls => urls![0])
            .ToList();
        var existingUrls = route.Links.Select(l => l.Url).ToHashSet();
        var toAppend = validLinks
            .Where(u => !existingUrls.Contains(u))
            .Distinct()
            .Take(Math.Max(0, LinkRules.MaxLinks - route.Links.Count))
            .ToList();
        route.Links.AddRange(toAppend.Select(u => new Link { Url = u }));

        var topoImported = false;
        if (result.TopoImageUrl is not null && route.Topos.Count == 0)
        {
            topoImported = await TryImportTopoAsync(route, areas.SlugPath(route.AreaId), result.TopoImageUrl, context, cancellationToken);
        }

        route.EnrichedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("enrichment completed for route {RouteId}: appended {AppendedCount} link(s), topoImported={TopoImported}",
            routeId, toAppend.Count, topoImported);
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
            // The page it came from is still a fine link even when the image
            // itself doesn't clear the bar — but that's already in result.Links
            // if the model surfaced it, so there's nothing else to do here.
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
