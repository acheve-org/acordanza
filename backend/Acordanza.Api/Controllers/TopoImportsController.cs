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

// Upload a topo photo, let a vision model read it, and create or match the route it
// describes (plan 30). The enrichment worker's sibling — same queue-and-job shape, same
// "don't write anything the model didn't demonstrably see" discipline — but pointed at
// an image the user supplied rather than at the open web.
//
// Gated on Permissions.RouteCreate, deliberately *not* a new permission and *not*
// route.enrich: that one is admin-only because plan 18's spend was unbounded per run and
// the button was an optional extra. Here the call is the feature, it costs about $0.05,
// and the write lands as Pending for review — someone trusted to create a route by
// typing it is trusted to create one by photographing it. The bound is the per-IP
// rate-limit policy below, which caps abuse without inventing a role.
[Authorize]
[ApiController]
[Route("api/topo-imports")]
public class TopoImportsController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    AreaHierarchyService areaHierarchy,
    BlobStorageService blobStorage,
    TopoImportQueue topoImportQueue) : ControllerBase
{
    [Authorize(Policy = Permissions.RouteCreate)]
    [HttpPost]
    [EnableRateLimiting("topo-import")]
    [RequestSizeLimit(MediaUploadService.MaxImageBytes)]
    public async Task<ActionResult<TopoImportDto>> CreateImport(IFormFile? file, [FromForm] int areaId)
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
        var blobName = $"{TopoImportKeys.StagingPrefix}{Guid.NewGuid():N}{extension}";
        await blobStorage.UploadAsync(blobName, file.OpenReadStream(), file.ContentType);

        var import = new TopoImport
        {
            BlobName = blobName,
            ContentType = file.ContentType,
            FileName = file.FileName,
            AreaId = areaId,
            UploadedByUserId = userId,
            Status = TopoImportStatus.Pending
        };
        db.TopoImports.Add(import);
        await db.SaveChangesAsync();

        try
        {
            await topoImportQueue.EnqueueAsync(import.Id);
        }
        catch (RequestFailedException)
        {
            // Same call RoutesController.EnrichRoute makes on a failed enqueue: nothing
            // else usefully happened in this request, so unwind rather than leave a row
            // that will sit Pending forever with no worker ever coming for it.
            db.TopoImports.Remove(import);
            await db.SaveChangesAsync();
            await blobStorage.DeleteAsync(blobName);
            return StatusCode(503, new { error = "No se pudo iniciar la lectura del topo, inténtalo de nuevo" });
        }

        var areas = await areaHierarchy.GetAsync();
        return Accepted(ToDto(import, areas.NamePath(import.AreaId), null));
    }

    [HttpGet("mine")]
    public async Task<ActionResult<List<TopoImportDto>>> GetMyImports()
    {
        var userId = userManager.GetUserId(User)!;
        var areas = await areaHierarchy.GetAsync();

        // Scoped to the caller by GetUserId, never by an id from the client — same
        // pattern as ContributionsController, hence no "mine" parameter to forge.
        var imports = await db.TopoImports
            .AsNoTracking()
            .Include(i => i.Route)
            .Where(i => i.UploadedByUserId == userId)
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .ToListAsync();

        return imports
            .Select(i => ToDto(i, areas.NamePath(i.AreaId), i.Route is null ? null : areas.SlugPath(i.Route.AreaId)))
            .ToList();
    }

    private static TopoImportDto ToDto(TopoImport import, string areaNamePath, string? routeAreaSlugPath) => new(
        import.Id,
        import.FileName,
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
