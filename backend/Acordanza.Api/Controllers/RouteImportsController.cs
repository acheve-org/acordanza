using Azure;
using Acordanza.Api.Authorization;
using Acordanza.Api.Dtos;
using Acordanza.Api.Services;
using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Acordanza.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Controllers;

// Hand the worker something to read a route off — a topo photo (plan 30) or the URL of a
// page (plan 31) — and let it create or match the route it describes. The enrichment
// worker's sibling: same queue-and-job shape, same "don't write anything the reader
// didn't demonstrably see" discipline, but pointed at an artifact the user supplied
// rather than at the open web.
//
// Two sibling sub-paths rather than plan 31 §1's single endpoint distinguished by content
// type, and the reason is the OpenAPI document: an operation is keyed by path *and*
// method, so two actions on `POST /route-imports` produce one documented operation and
// silently drop the other — the generated frontend types would then describe an API that
// is missing half of itself, and `openapi-contract-check` would happily pass. Everything
// the plan's one-endpoint shape was for is unaffected: the two kinds still share a row, a
// queue, a processor, a rate-limit bucket and a page, and diverge only in how the reader
// turns the artifact into a candidate (§2).
//
// Gated on Permissions.RouteCreate, deliberately *not* a new permission and *not*
// route.enrich: that one is admin-only because plan 18's spend was unbounded per run and
// the button was an optional extra. Here the call is the feature, it costs about $0.05,
// and the write lands as Pending for review — someone trusted to create a route by
// typing it is trusted to create one by photographing it or by pasting the post about it.
// The bound is the per-IP rate-limit policy below, which caps abuse without inventing a
// role — and, for the Url kind, caps how many outbound fetches one caller can ask for.
[Authorize]
[ApiController]
[Route("api/route-imports")]
public class RouteImportsController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    AreaHierarchyService areaHierarchy,
    BlobStorageService blobStorage,
    RouteImportQueue routeImportQueue) : ControllerBase
{
    [Authorize(Policy = Permissions.RouteCreate)]
    [HttpPost("image")]
    [EnableRateLimiting("route-import")]
    [RequestSizeLimit(MediaUploadService.MaxImageBytes)]
    public async Task<ActionResult<RouteImportDto>> CreateImageImport(IFormFile? file, [FromForm] int areaId)
    {
        var validationError = ImageValidation.ValidateImage(file);
        if (validationError is not null) return BadRequest(new { error = validationError.Message });

        if (!await db.Areas.AnyAsync(a => a.Id == areaId))
        {
            return BadRequest(new { error = "el sector no existe" });
        }

        var userId = userManager.GetUserId(User)!;

        // A GUID rather than the row id: the blob has to be written before the row exists
        // (the row records its name), and the sweep rule on this prefix means a name that
        // outlives a crashed request costs nothing.
        var extension = Path.GetExtension(file!.FileName);
        var blobName = $"{RouteImportKeys.StagingPrefix}{Guid.NewGuid():N}{extension}";
        await blobStorage.UploadAsync(blobName, file.OpenReadStream(), file.ContentType);

        var import = new RouteImport
        {
            Kind = RouteImportKind.Image,
            BlobName = blobName,
            ContentType = file.ContentType,
            FileName = file.FileName,
            AreaId = areaId,
            SubmittedByUserId = userId,
            Status = RouteImportStatus.Pending
        };

        return await SaveAndEnqueueAsync(
            import,
            onUnwind: () => blobStorage.DeleteAsync(blobName),
            enqueueError: "No se pudo iniciar la lectura del topo, inténtalo de nuevo");
    }

    [Authorize(Policy = Permissions.RouteCreate)]
    [HttpPost("url")]
    [EnableRateLimiting("route-import")]
    public async Task<ActionResult<RouteImportDto>> CreateUrlImport([FromBody] CreateUrlImportRequest request)
    {
        // Deliberately *syntactic* only — http(s)-only, trimmed, length-capped, the same
        // allowlist a human-entered route link goes through. The network-level guard (a
        // private address, a redirect into one, a non-HTML body) is the worker's, in
        // PageFetcher: any check made here is advisory by the time the worker connects,
        // and DNS can change in between. What this *does* buy is telling the user their
        // typo is a typo now, rather than after a two-minute wait for a Discarded row
        // saying the same thing.
        var (urls, linkError) = LinkRules.Resolve([request.Url ?? ""]);
        if (linkError is not null) return BadRequest(new { error = linkError });
        if (urls is not [var url]) return BadRequest(new { error = "escribe el enlace de la página" });

        if (!await db.Areas.AnyAsync(a => a.Id == request.AreaId))
        {
            return BadRequest(new { error = "el sector no existe" });
        }

        var import = new RouteImport
        {
            Kind = RouteImportKind.Url,
            // Canonicalised here rather than in the worker so the duplicate short-circuit
            // (plan 31 §5) compares stored values, and so a re-paste carrying a tracking
            // parameter is the same submission.
            SourceUrl = UrlCanonicalizer.Canonical(url),
            AreaId = request.AreaId,
            SubmittedByUserId = userManager.GetUserId(User)!,
            Status = RouteImportStatus.Pending
        };

        return await SaveAndEnqueueAsync(
            import,
            onUnwind: () => Task.CompletedTask,
            enqueueError: "No se pudo iniciar la lectura de la página, inténtalo de nuevo");
    }

    [HttpGet("mine")]
    public async Task<ActionResult<List<RouteImportDto>>> GetMyImports()
    {
        var userId = userManager.GetUserId(User)!;
        var areas = await areaHierarchy.GetAsync();

        // Scoped to the caller by GetUserId, never by an id from the client — same
        // pattern as ContributionsController, hence no "mine" parameter to forge.
        var imports = await db.RouteImports
            .AsNoTracking()
            .Include(i => i.Route)
            .Where(i => i.SubmittedByUserId == userId)
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .ToListAsync();

        return imports
            .Select(i => ToDto(i, areas.NamePath(i.AreaId), i.Route is null ? null : areas.SlugPath(i.Route.AreaId)))
            .ToList();
    }

    // The half both kinds share: persist the row, enqueue it, and unwind completely if
    // the enqueue fails — the same call RoutesController.EnrichRoute makes, since nothing
    // else usefully happened in the request and a row nobody will ever drain would sit
    // Pending forever.
    private async Task<ActionResult<RouteImportDto>> SaveAndEnqueueAsync(
        RouteImport import, Func<Task> onUnwind, string enqueueError)
    {
        db.RouteImports.Add(import);
        await db.SaveChangesAsync();

        try
        {
            await routeImportQueue.EnqueueAsync(import.Id);
        }
        catch (RequestFailedException)
        {
            db.RouteImports.Remove(import);
            await db.SaveChangesAsync();
            await onUnwind();
            return StatusCode(503, new { error = enqueueError });
        }

        var areas = await areaHierarchy.GetAsync();
        return Accepted(ToDto(import, areas.NamePath(import.AreaId), null));
    }

    private static RouteImportDto ToDto(RouteImport import, string areaNamePath, string? routeAreaSlugPath) => new(
        import.Id,
        import.Kind.ToString().ToLowerInvariant(),
        import.FileName,
        import.SourceUrl,
        import.AreaId,
        areaNamePath,
        import.CreatedAt,
        import.Status.ToString().ToLowerInvariant(),
        import.Outcome,
        import.RouteId,
        import.Route?.Name,
        import.Route?.Slug,
        routeAreaSlugPath,
        import.RouteWasCreated,
        import.ProcessedAt);
}
