using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Acordanza.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/areas")]
public class AreasController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    BlobStorageService blobStorage,
    MediaUploadService mediaUpload,
    AreaHierarchyService areaHierarchy,
    AreaExpertAuthorizer areaExpertAuthorizer) : ControllerBase
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;

    [HttpGet]
    public async Task<ActionResult<List<AreaDto>>> GetAreas(
        [FromQuery] double? minLat, [FromQuery] double? minLng, [FromQuery] double? maxLat, [FromQuery] double? maxLng,
        [FromQuery] bool includeUnvalidated = false)
    {
        if (includeUnvalidated && !User.HasClaim("permission", Permissions.AreaValidate))
        {
            return Forbid();
        }

        var query = db.Areas.AsNoTracking();
        if (!includeUnvalidated)
        {
            query = query.Where(ValidationVisibility.Areas(userManager.GetUserId(User)!));
        }

        // A bounding box (as sent by the map view) restricts results to areas with
        // geographic info placed inside it; omitting it returns every area, which
        // callers like the area picker and the areas management page need.
        if (minLat is not null && minLng is not null && maxLat is not null && maxLng is not null)
        {
            var matchedIds = await query
                .Where(a =>
                    a.CentroidLat != null && a.CentroidLng != null &&
                    a.CentroidLat >= minLat && a.CentroidLat <= maxLat &&
                    a.CentroidLng >= minLng && a.CentroidLng <= maxLng)
                .Select(a => a.Id)
                .ToListAsync();

            // A matched subarea is useless to AreaMap without its top-level ancestor
            // (needed to resolve the active hierarchy), even if that ancestor's own
            // centroid falls outside the box — so pull ancestors in regardless of bounds.
            var hierarchy = await areaHierarchy.GetAsync();
            var idsToInclude = new HashSet<int>(matchedIds);
            foreach (var id in matchedIds)
            {
                foreach (var ancestor in hierarchy.Ancestors(id))
                {
                    idsToInclude.Add(ancestor.Id);
                }
            }

            query = query.Where(a => idsToInclude.Contains(a.Id));
        }

        var areaEntities = await query
            .OrderBy(a => a.Name)
            .Include(a => a.DecidedBy)
            .ToListAsync();

        return Ok(areaEntities.Select(ToAreaDto).ToList());
    }

    // Returns only the root areas (no ParentAreaId), alphabetically ordered and
    // paginated, for the areas-management tree — see GetAreaChildren for lazy-loading
    // a node's children. GetAreas above remains the unfiltered/unpaginated source of
    // truth for callers (map, AreaPicker, AreaPolygonModal) that need every area at
    // once — all of which now only call it once their dropdown/modal actually opens.
    [HttpGet("roots")]
    public async Task<ActionResult<PagedAreaNodesResponse>> GetRootAreas(
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize, [FromQuery] bool includeUnvalidated = false)
    {
        if (includeUnvalidated && !User.HasClaim("permission", Permissions.AreaValidate))
        {
            return Forbid();
        }

        if (page < 1) page = 1;
        if (pageSize is < 1 or > MaxPageSize) pageSize = DefaultPageSize;

        var query = db.Areas.AsNoTracking().Where(a => a.ParentAreaId == null);
        if (!includeUnvalidated)
        {
            query = query.Where(ValidationVisibility.Areas(userManager.GetUserId(User)!));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(a => a.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            // CentroidLat is recomputed (or nulled) whenever the polygon is saved, so it
            // doubles as a plain-column "has a polygon" flag without reading the JSON vertices.
            .Select(a => new AreaNodeDto(a.Id, a.Name, a.Slug, a.Children.Any(), a.CentroidLat != null))
            .ToListAsync();

        return Ok(new PagedAreaNodesResponse(items, totalCount, page, pageSize));
    }

    [HttpGet("{id}/children")]
    public async Task<ActionResult<List<AreaNodeDto>>> GetAreaChildren(int id, [FromQuery] bool includeUnvalidated = false)
    {
        if (includeUnvalidated && !User.HasClaim("permission", Permissions.AreaValidate))
        {
            return Forbid();
        }

        if (!await db.Areas.AnyAsync(a => a.Id == id))
        {
            return NotFound(new { error = "el sector no existe" });
        }

        var query = db.Areas.AsNoTracking().Where(a => a.ParentAreaId == id);
        if (!includeUnvalidated)
        {
            query = query.Where(ValidationVisibility.Areas(userManager.GetUserId(User)!));
        }

        var children = await query
            .OrderBy(a => a.Name)
            .Select(a => new AreaNodeDto(a.Id, a.Name, a.Slug, a.Children.Any(), a.CentroidLat != null))
            .ToListAsync();

        return Ok(children);
    }

    // The review queue's view of not-yet-decided areas. NamePath is resolved here
    // (AreaHierarchyService.NamePath) rather than by the frontend downloading every
    // area to compute breadcrumbs client-side — see PendingTasksContext.
    [Authorize(Policy = Permissions.AreaValidate)]
    [HttpGet("pending")]
    public async Task<ActionResult<List<AreaPendingDto>>> GetPendingAreas()
    {
        var hierarchy = await areaHierarchy.GetAsync();
        var pending = await db.Areas.AsNoTracking()
            .Include(a => a.AddedBy)
            .Where(a => a.ValidationStatus == ReviewStatus.Pending)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();

        return Ok(pending
            .Select(a => new AreaPendingDto(a.Id, a.Name, hierarchy.NamePath(a.Id), a.ParentAreaId, a.CreatedAt, a.AddedBy?.Name))
            .ToList());
    }

    [Authorize(Policy = Permissions.AreaCreate)]
    [HttpPost]
    public async Task<ActionResult<AreaDto>> CreateArea(CreateAreaRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "el nombre es obligatorio" });
        }

        if (request.ParentAreaId is int parentId && !await db.Areas.AnyAsync(a => a.Id == parentId))
        {
            return BadRequest(new { error = "el sector padre no existe" });
        }

        var name = request.Name.Trim();
        var isDuplicate = await db.Areas.AnyAsync(a =>
            a.ParentAreaId == request.ParentAreaId && a.Name.ToLower() == name.ToLower());
        if (isDuplicate)
        {
            return Conflict(new { error = "ya existe un sector con ese nombre en este nivel" });
        }

        var slug = await GenerateUniqueAreaSlugAsync(request.ParentAreaId, name);
        var userId = userManager.GetUserId(User)!;
        var area = new Area { Name = name, Slug = slug, ParentAreaId = request.ParentAreaId, AddedByUserId = userId };

        // A creator who can also validate areas (moderator/admin) has their new
        // area auto-validated; everyone else's (users, first-ascensionists) is
        // left pending for a moderator/admin to review via POST /{id}/validate.
        if (User.HasClaim("permission", Permissions.AreaValidate))
        {
            Validation.MarkValidated(area, userId);
        }

        db.Areas.Add(area);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The AnyAsync check above is only a friendly pre-check — this catches the rare
            // case of two concurrent creates racing past it for the same parent/name.
            return Conflict(new { error = "ya existe un sector con ese nombre en este nivel" });
        }

        return Ok(new AreaDto(
            area.Id,
            area.Name,
            area.Slug,
            area.ParentAreaId,
            [],
            null,
            null,
            area.ValidationStatus.ToString().ToLowerInvariant(),
            area.DecidedAt,
            await GetDecidedByNameAsync(area.DecidedByUserId),
            area.RejectionReason));
    }

    [Authorize(Policy = Permissions.AreaEdit)]
    [HttpPut("{id}")]
    public async Task<ActionResult<AreaDto>> UpdateArea(int id, UpdateAreaRequest request)
    {
        var area = await db.Areas.FindAsync(id);
        if (area is null)
        {
            return NotFound(new { error = "el sector no existe" });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "el nombre es obligatorio" });
        }

        if (request.ParentAreaId is int parentId)
        {
            if (parentId == id)
            {
                return BadRequest(new { error = "un sector no puede ser su propio padre" });
            }

            if (!await db.Areas.AnyAsync(a => a.Id == parentId))
            {
                return BadRequest(new { error = "el sector padre no existe" });
            }

            // Walk up from the proposed parent to make sure `id` isn't among its
            // ancestors, which would otherwise turn the tree into a cycle.
            var ancestorId = (int?)parentId;
            while (ancestorId is not null)
            {
                if (ancestorId == id)
                {
                    return BadRequest(new { error = "el sector padre no puede ser un descendiente de este sector" });
                }

                ancestorId = await db.Areas.Where(a => a.Id == ancestorId).Select(a => a.ParentAreaId).FirstOrDefaultAsync();
            }
        }

        var newName = request.Name.Trim();
        var changed = newName != area.Name || request.ParentAreaId != area.ParentAreaId;

        area.Name = newName;
        area.ParentAreaId = request.ParentAreaId;

        Validation.InvalidateIfChanged(area, changed);

        await db.SaveChangesAsync();

        return Ok(new AreaDto(
            area.Id,
            area.Name,
            area.Slug,
            area.ParentAreaId,
            area.Polygon.Select(p => new GeoPointDto(p.Lat, p.Lng)).ToList(),
            area.CentroidLat,
            area.CentroidLng,
            area.ValidationStatus.ToString().ToLowerInvariant(),
            area.DecidedAt,
            await GetDecidedByNameAsync(area.DecidedByUserId),
            area.RejectionReason));
    }

    [Authorize(Policy = Permissions.AreaDelete)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteArea(int id)
    {
        var area = await db.Areas.FindAsync(id);
        if (area is null)
        {
            return NotFound(new { error = "el sector no existe" });
        }

        var idsToDelete = (await areaHierarchy.GetAsync()).DescendantIds(id);

        if (await db.Routes.AnyAsync(r => idsToDelete.Contains(r.AreaId)))
        {
            return BadRequest(new { error = "el sector o alguno de sus subsectores tiene vías asociadas; muévelas o elimínalas primero" });
        }

        // AreaMedia rows cascade-delete with their area (see AppDbContext), but that's
        // DB-only — their blobs won't clean themselves up, so their names are captured
        // now (while still queryable) but not actually deleted from storage until after
        // the area removal below has committed. Deleting the blobs first would leave
        // them orphaned-gone if SaveChangesAsync then failed (e.g. a route or child area
        // inserted concurrently after the AnyAsync check above trips the Restrict FK).
        var mediaToDelete = await db.AreaMedia.Where(m => idsToDelete.Contains(m.AreaId)).ToListAsync();

        var areasToDelete = await db.Areas.Where(a => idsToDelete.Contains(a.Id)).ToListAsync();
        db.Areas.RemoveRange(areasToDelete);
        await db.SaveChangesAsync();

        foreach (var media in mediaToDelete)
        {
            await blobStorage.DeleteAsync(media.BlobName);
        }

        return NoContent();
    }

    // Returns a single area resolved by its full slug path (e.g. "pena-rua/sector-1") —
    // see AreaHierarchy.ResolveSlugPath for the level-by-level walk.
    [HttpGet("by-slug-path/{*path}")]
    public async Task<ActionResult<AreaDetailDto>> GetAreaBySlugPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return NotFound(new { error = "el sector no existe" });
        }

        var hierarchy = await areaHierarchy.GetAsync();
        var resolved = hierarchy.ResolveSlugPath(segments);
        if (resolved is null)
        {
            return NotFound(new { error = "el sector no existe" });
        }

        var area = await db.Areas.Include(a => a.DecidedBy).FirstOrDefaultAsync(a => a.Id == resolved.Id);
        if (area is null)
        {
            return NotFound(new { error = "el sector no existe" });
        }

        var ancestors = hierarchy.Ancestors(area.Id)
            .Select(a => new AreaAncestorDto(a.Id, a.Name, a.Slug))
            .ToList();

        var callerId = userManager.GetUserId(User)!;
        var children = await db.Areas.AsNoTracking()
            .Where(a => a.ParentAreaId == area.Id)
            .Where(ValidationVisibility.Areas(callerId))
            .OrderBy(a => a.Name)
            .Select(a => new AreaChildDto(a.Id, a.Name, a.Slug, a.Children.Any()))
            .ToListAsync();

        var media = await db.AreaMedia.AsNoTracking()
            .Where(m => m.AreaId == area.Id)
            .Include(m => m.UploadedBy)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
        var mediaDtos = new List<TopoDto>();
        foreach (var m in media)
        {
            var url = await blobStorage.GetReadSasUrlAsync(m.BlobName, MediaUploadService.ImageUrlLifetime);
            mediaDtos.Add(new TopoDto(m.Id, url, m.FileName, m.CreatedAt, m.UploadedBy.Name));
        }

        var canManageMedia = await areaExpertAuthorizer.CanActOnAreaAsync(User, Permissions.AreaEdit, area.Id);

        return Ok(new AreaDetailDto(
            area.Id,
            area.Name,
            area.Slug,
            area.ParentAreaId,
            ancestors,
            children,
            area.ValidationStatus.ToString().ToLowerInvariant(),
            area.DecidedAt,
            area.DecidedBy?.Name,
            area.RejectionReason,
            canManageMedia,
            mediaDtos));
    }

    [HttpPost("{id:int}/media")]
    [RequestSizeLimit(MediaUploadService.MaxImageBytes)]
    public async Task<ActionResult<TopoDto>> UploadAreaMedia(int id, IFormFile? file)
    {
        var validationError = mediaUpload.ValidateImage(file);
        if (validationError is not null) return BadRequest(new { error = validationError.Message });

        var area = await db.Areas.FirstOrDefaultAsync(a => a.Id == id);
        if (area is null) return NotFound(new { error = "No encontrado" });

        if (!await areaExpertAuthorizer.CanActOnAreaAsync(User, Permissions.AreaEdit, id))
        {
            return Forbid();
        }

        var currentUser = await userManager.GetUserAsync(User);
        if (currentUser is null) return Unauthorized();

        // Named after the area's own slug plus a numeric suffix, same convention as
        // route topos (RoutesController.UploadTopo). The "/_media-" segment (not just
        // "-") is deliberate: without it, this area's prefix would be
        // "{ParentPath}/{area.Slug}-", identical to a route topo's
        // "{ParentPath}/{routeSlug}-" prefix whenever a route in the parent area happens
        // to slugify to the same name as this area — Slugify.ToSlug never emits "_", so
        // no route/area slug can ever collide with this reserved marker.
        var extension = Path.GetExtension(file!.FileName);
        var hierarchy = await areaHierarchy.GetAsync();
        var blobPrefix = $"{hierarchy.SlugPath(id)}/_media-";
        var usedBlobNames = await db.AreaMedia.AsNoTracking()
            .Where(m => m.BlobName.StartsWith(blobPrefix))
            .Select(m => m.BlobName)
            .ToListAsync();

        var blobName = await mediaUpload.UploadNumberedAsync(blobPrefix, extension, usedBlobNames, file);
        if (blobName is null)
        {
            return Conflict(new { error = "no se pudo subir la imagen, inténtalo de nuevo" });
        }

        var media = new AreaMedia
        {
            AreaId = area.Id,
            BlobName = blobName,
            ContentType = file.ContentType,
            FileName = file.FileName,
            UploadedByUserId = currentUser.Id
        };
        db.AreaMedia.Add(media);
        await mediaUpload.SaveOrCleanupAsync(blobName, () => db.SaveChangesAsync());

        var url = await blobStorage.GetReadSasUrlAsync(media.BlobName, MediaUploadService.ImageUrlLifetime);
        return Ok(new TopoDto(media.Id, url, media.FileName, media.CreatedAt, currentUser.Name));
    }

    // Two sibling areas can never share a Name (checked above), but different names can
    // still slugify to the same value (e.g. "Peña Rúa" vs "Pena-Rua") — append "-2", "-3"
    // etc. until the slug is actually unique among this area's siblings, rather than
    // letting two areas collide onto the same topo storage prefix.
    private async Task<string> GenerateUniqueAreaSlugAsync(int? parentAreaId, string name)
    {
        var baseSlug = Slugify.ToSlug(name);
        var slug = baseSlug;
        var suffix = 2;
        while (await db.Areas.AnyAsync(a => a.ParentAreaId == parentAreaId && a.Slug == slug))
        {
            slug = $"{baseSlug}-{suffix}";
            suffix++;
        }
        return slug;
    }

    private async Task<string?> GetDecidedByNameAsync(string? decidedByUserId) =>
        decidedByUserId is null
            ? null
            : await db.Users.Where(u => u.Id == decidedByUserId).Select(u => u.Name).FirstOrDefaultAsync();

    private static AreaDto ToAreaDto(Area a) => new(
        a.Id,
        a.Name,
        a.Slug,
        a.ParentAreaId,
        a.Polygon.Select(p => new GeoPointDto(p.Lat, p.Lng)).ToList(),
        a.CentroidLat,
        a.CentroidLng,
        a.ValidationStatus.ToString().ToLowerInvariant(),
        a.DecidedAt,
        a.DecidedBy?.Name,
        a.RejectionReason);

    [Authorize(Policy = Permissions.AreaEdit)]
    [HttpPut("{id}/coordinates")]
    public async Task<ActionResult<AreaDto>> UpdateAreaCoordinates(int id, UpdateAreaCoordinatesRequest request)
    {
        var area = await db.Areas.FindAsync(id);
        if (area is null)
        {
            return NotFound(new { error = "el sector no existe" });
        }

        if (request.Polygon.Count != 0 && request.Polygon.Count < 3)
        {
            return BadRequest(new { error = "el polígono necesita al menos 3 puntos" });
        }

        var newPolygon = request.Polygon.Select(p => new GeoPoint { Lat = p.Lat, Lng = p.Lng }).ToList();
        var changed = newPolygon.Count != area.Polygon.Count ||
            newPolygon.Zip(area.Polygon, (a, b) => a.Lat != b.Lat || a.Lng != b.Lng).Any(different => different);

        area.Polygon = newPolygon;

        if (area.Polygon.Count == 0)
        {
            area.CentroidLat = null;
            area.CentroidLng = null;
        }
        else
        {
            area.CentroidLat = area.Polygon.Average(p => p.Lat);
            area.CentroidLng = area.Polygon.Average(p => p.Lng);
        }

        Validation.InvalidateIfChanged(area, changed);

        await db.SaveChangesAsync();

        return Ok(new AreaDto(
            area.Id,
            area.Name,
            area.Slug,
            area.ParentAreaId,
            area.Polygon.Select(p => new GeoPointDto(p.Lat, p.Lng)).ToList(),
            area.CentroidLat,
            area.CentroidLng,
            area.ValidationStatus.ToString().ToLowerInvariant(),
            area.DecidedAt,
            await GetDecidedByNameAsync(area.DecidedByUserId),
            area.RejectionReason));
    }

    [Authorize(Policy = Permissions.AreaValidate)]
    [HttpPost("{id:int}/validate")]
    public async Task<ActionResult> ValidateArea(int id)
    {
        var area = await db.Areas.FindAsync(id);
        if (area is null) return NotFound(new { error = "el sector no existe" });

        Validation.MarkValidated(area, userManager.GetUserId(User)!);

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [Authorize(Policy = Permissions.AreaValidate)]
    [HttpPost("{id:int}/reject")]
    public async Task<ActionResult> RejectArea(int id, RejectValidationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "el motivo es obligatorio" });
        }

        var area = await db.Areas.FindAsync(id);
        if (area is null) return NotFound(new { error = "el sector no existe" });

        Validation.MarkRejected(area, userManager.GetUserId(User)!, request.Reason.Trim());

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
