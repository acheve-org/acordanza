using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Acordanza.Api.Services;
using Azure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/areas")]
public class AreasController(AppDbContext db, UserManager<ApplicationUser> userManager, BlobStorageService blobStorage)
    : ControllerBase
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;

    private static readonly HashSet<string> AllowedMediaContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private const long MaxMediaBytes = 15 * 1024 * 1024;
    private static readonly TimeSpan MediaUrlLifetime = TimeSpan.FromMinutes(15);

    private sealed record AreaNode(int Id, string Name, string Slug, int? ParentAreaId);

    [HttpGet]
    public async Task<ActionResult<List<AreaDto>>> GetAreas(
        [FromQuery] double? minLat, [FromQuery] double? minLng, [FromQuery] double? maxLat, [FromQuery] double? maxLng)
    {
        var query = db.Areas.AsNoTracking();

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
            var parentById = await db.Areas.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.ParentAreaId);
            var idsToInclude = new HashSet<int>(matchedIds);
            foreach (var id in matchedIds)
            {
                var current = parentById.GetValueOrDefault(id);
                while (current is int parentId && idsToInclude.Add(parentId))
                {
                    current = parentById.GetValueOrDefault(parentId);
                }
            }

            query = query.Where(a => idsToInclude.Contains(a.Id));
        }

        var areas = await query
            .OrderBy(a => a.Name)
            .Select(a => new AreaDto(
                a.Id,
                a.Name,
                a.Slug,
                a.ParentAreaId,
                a.Polygon.Select(p => new GeoPointDto(p.Lat, p.Lng)).ToList(),
                a.CentroidLat,
                a.CentroidLng,
                a.ValidatedAt,
                a.ValidatedBy != null ? a.ValidatedBy.Name : null))
            .ToListAsync();

        return Ok(areas);
    }

    // Returns only the root areas (no ParentAreaId), alphabetically ordered and
    // paginated, for the areas-management tree — see GetAreaChildren for lazy-loading
    // a node's children. GetAreas above remains the unfiltered/unpaginated source of
    // truth for callers (map, AreaPicker, polygon section) that need every area at once.
    [HttpGet("roots")]
    public async Task<ActionResult<PagedAreaNodesResponse>> GetRootAreas([FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > MaxPageSize) pageSize = DefaultPageSize;

        var query = db.Areas.AsNoTracking().Where(a => a.ParentAreaId == null);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(a => a.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AreaNodeDto(a.Id, a.Name, a.Slug, a.Children.Any()))
            .ToListAsync();

        return Ok(new PagedAreaNodesResponse(items, totalCount, page, pageSize));
    }

    [HttpGet("{id}/children")]
    public async Task<ActionResult<List<AreaNodeDto>>> GetAreaChildren(int id)
    {
        if (!await db.Areas.AnyAsync(a => a.Id == id))
        {
            return NotFound(new { error = "el sector no existe" });
        }

        var children = await db.Areas.AsNoTracking()
            .Where(a => a.ParentAreaId == id)
            .OrderBy(a => a.Name)
            .Select(a => new AreaNodeDto(a.Id, a.Name, a.Slug, a.Children.Any()))
            .ToListAsync();

        return Ok(children);
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
        var area = new Area { Name = name, Slug = slug, ParentAreaId = request.ParentAreaId };

        // A creator who can also validate areas (moderator/admin) has their new
        // area auto-validated; everyone else's (users, first-ascensionists) is
        // left pending for a moderator/admin to review via POST /{id}/validate.
        if (User.HasClaim("permission", Permissions.AreaValidate))
        {
            area.ValidatedAt = DateTimeOffset.UtcNow;
            area.ValidatedByUserId = userManager.GetUserId(User)!;
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
            area.ValidatedAt,
            await GetValidatedByNameAsync(area.ValidatedByUserId)));
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

        // An edit invalidates any prior validation of the area's info, same rule
        // as RoutesController.UpdateRoute — but only when something actually
        // changed, so re-saving the form as-is doesn't generate a spurious
        // pending-validation task.
        if (changed)
        {
            area.ValidatedAt = null;
            area.ValidatedByUserId = null;
        }

        await db.SaveChangesAsync();

        return Ok(new AreaDto(
            area.Id,
            area.Name,
            area.Slug,
            area.ParentAreaId,
            area.Polygon.Select(p => new GeoPointDto(p.Lat, p.Lng)).ToList(),
            area.CentroidLat,
            area.CentroidLng,
            area.ValidatedAt,
            await GetValidatedByNameAsync(area.ValidatedByUserId)));
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

        var allAreaIds = await db.Areas.AsNoTracking()
            .Select(a => new AreaIdParent(a.Id, a.ParentAreaId))
            .ToListAsync();
        var idsToDelete = DescendantAreaIds(allAreaIds, id);

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

    // Returns a single area resolved by its full slug path (e.g. "pena-rua/sector-1"),
    // walked level by level since Area.Slug is only unique among siblings — see
    // RoutesController.AreaSlugPath for the same walk used to build blob prefixes.
    [HttpGet("by-slug-path/{*path}")]
    public async Task<ActionResult<AreaDetailDto>> GetAreaBySlugPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return NotFound(new { error = "el sector no existe" });
        }

        var areas = await db.Areas.AsNoTracking()
            .Select(a => new AreaNode(a.Id, a.Name, a.Slug, a.ParentAreaId))
            .ToListAsync();
        var areaById = areas.ToDictionary(a => a.Id);

        int? currentParentId = null;
        AreaNode? resolved = null;
        foreach (var segment in segments)
        {
            resolved = areas.FirstOrDefault(a => a.ParentAreaId == currentParentId && a.Slug == segment);
            if (resolved is null)
            {
                return NotFound(new { error = "el sector no existe" });
            }
            currentParentId = resolved.Id;
        }

        var area = await db.Areas.FirstOrDefaultAsync(a => a.Id == resolved!.Id);
        if (area is null)
        {
            return NotFound(new { error = "el sector no existe" });
        }

        var ancestors = new List<AreaAncestorDto>();
        var ancestorId = area.ParentAreaId;
        while (ancestorId is int aid && areaById.TryGetValue(aid, out var ancestor))
        {
            ancestors.Insert(0, new AreaAncestorDto(ancestor.Id, ancestor.Name, ancestor.Slug));
            ancestorId = ancestor.ParentAreaId;
        }

        var children = await db.Areas.AsNoTracking()
            .Where(a => a.ParentAreaId == area.Id)
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
            var url = await blobStorage.GetReadSasUrlAsync(m.BlobName, MediaUrlLifetime);
            mediaDtos.Add(new TopoDto(m.Id, url, m.FileName, m.CreatedAt, m.UploadedBy.Name));
        }

        var canManageMedia = await BuildCanManageAreaMediaCheckAsync(areaById, area.Id);

        return Ok(new AreaDetailDto(
            area.Id,
            area.Name,
            area.Slug,
            area.ParentAreaId,
            ancestors,
            children,
            area.ValidatedAt,
            await GetValidatedByNameAsync(area.ValidatedByUserId),
            canManageMedia,
            mediaDtos));
    }

    [HttpPost("{id:int}/media")]
    [RequestSizeLimit(MaxMediaBytes)]
    public async Task<ActionResult<TopoDto>> UploadAreaMedia(int id, IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "la imagen es obligatoria" });
        }
        if (!AllowedMediaContentTypes.Contains(file.ContentType))
        {
            return BadRequest(new { error = "el formato de la imagen no es válido (usa JPEG, PNG o WebP)" });
        }
        if (file.Length > MaxMediaBytes)
        {
            return BadRequest(new { error = "la imagen es demasiado grande (máximo 15 MB)" });
        }

        var area = await db.Areas.FirstOrDefaultAsync(a => a.Id == id);
        if (area is null) return NotFound(new { error = "No encontrado" });

        var areas = await db.Areas.AsNoTracking()
            .Select(a => new AreaNode(a.Id, a.Name, a.Slug, a.ParentAreaId))
            .ToListAsync();
        var areaById = areas.ToDictionary(a => a.Id);

        var canManageMedia = await BuildCanManageAreaMediaCheckAsync(areaById, id);
        if (!canManageMedia)
        {
            return Forbid();
        }

        var currentUser = await userManager.GetUserAsync(User);
        if (currentUser is null) return Unauthorized();

        // Named after the area's own slug plus a numeric suffix, same convention as
        // route topos (RoutesController.UploadTopo) — the next number is the highest
        // one actually in use under this prefix, not just this area's media count.
        // The "/_media-" segment (not just "-") is deliberate: without it, this area's
        // prefix would be "{ParentPath}/{area.Slug}-", identical to a route topo's
        // "{ParentPath}/{routeSlug}-" prefix whenever a route in the parent area happens
        // to slugify to the same name as this area — Slugify.ToSlug never emits "_", so
        // no route/area slug can ever collide with this reserved marker.
        var extension = Path.GetExtension(file.FileName);
        var blobPrefix = $"{AreaSlugPath(areaById, id)}/_media-";
        var usedBlobNames = await db.AreaMedia.AsNoTracking()
            .Where(m => m.BlobName.StartsWith(blobPrefix))
            .Select(m => m.BlobName)
            .ToListAsync();
        var nextNumber = HighestMediaNumber(usedBlobNames, blobPrefix) + 1;

        const int maxAttempts = 5;
        string? blobName = null;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var candidate = $"{blobPrefix}{nextNumber + attempt:D2}{extension}";
            try
            {
                await using var stream = file.OpenReadStream();
                await blobStorage.UploadAsync(candidate, stream, file.ContentType, overwrite: false);
                blobName = candidate;
                break;
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // Name taken by a racing upload — try the next number.
            }
        }

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
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The blob already landed in storage — if the row can't be saved (e.g. the
            // area or uploader was deleted concurrently), delete it too rather than
            // leaving an orphaned blob that occupies this name forever.
            await blobStorage.DeleteAsync(blobName);
            throw;
        }

        var url = await blobStorage.GetReadSasUrlAsync(media.BlobName, MediaUrlLifetime);
        return Ok(new TopoDto(media.Id, url, media.FileName, media.CreatedAt, currentUser.Name));
    }

    // Admins/moderators (area.edit) can manage any area's media; otherwise, only an
    // approved AreaExpert of the area's top-level ancestor can — same "ownership scoped
    // to a query, not a claim" pattern RoutesController.BuildCanValidateAreaCheckAsync uses.
    private async Task<bool> BuildCanManageAreaMediaCheckAsync(Dictionary<int, AreaNode> areaById, int areaId)
    {
        if (User.HasClaim("permission", Permissions.AreaEdit))
        {
            return true;
        }

        var userId = userManager.GetUserId(User)!;
        var expertAreaIds = await db.AreaExperts
            .Where(e => e.UserId == userId && e.Status == AreaExpertStatus.Approved)
            .Select(e => e.AreaId)
            .ToListAsync();

        if (expertAreaIds.Count == 0)
        {
            return false;
        }

        return expertAreaIds.Contains(TopLevelAreaId(areaById, areaId));
    }

    private static int TopLevelAreaId(Dictionary<int, AreaNode> areaById, int id)
    {
        var result = id;
        int? currentId = id;
        while (currentId is int aid && areaById.TryGetValue(aid, out var area))
        {
            result = aid;
            currentId = area.ParentAreaId;
        }
        return result;
    }

    // Same ancestor walk as GetAreaBySlugPath's Ancestors, but joining Slug (not Name)
    // with "/" — this is the blob-name prefix this area's own media is stored under.
    private static string AreaSlugPath(Dictionary<int, AreaNode> areaById, int id)
    {
        var parts = new List<string>();
        int? currentId = id;
        while (currentId is int aid && areaById.TryGetValue(aid, out var area))
        {
            parts.Insert(0, area.Slug);
            currentId = area.ParentAreaId;
        }
        return string.Join("/", parts);
    }

    // Parses the trailing "-{number}" out of each blob name (after the shared prefix,
    // before the extension) and returns the highest one found, or 0 if none/none parse.
    private static int HighestMediaNumber(List<string> blobNames, string prefix)
    {
        var highest = 0;
        foreach (var blobName in blobNames)
        {
            var numberPart = Path.GetFileNameWithoutExtension(blobName[prefix.Length..]);
            if (int.TryParse(numberPart, out var number) && number > highest)
            {
                highest = number;
            }
        }
        return highest;
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

    private async Task<string?> GetValidatedByNameAsync(string? validatedByUserId) =>
        validatedByUserId is null
            ? null
            : await db.Users.Where(u => u.Id == validatedByUserId).Select(u => u.Name).FirstOrDefaultAsync();

    private record AreaIdParent(int Id, int? ParentAreaId);

    // Breadth-first walk collecting `rootId` and every descendant, so callers can check
    // for routes linked anywhere in the subtree and delete the whole subtree at once —
    // EF's DeleteBehavior.Restrict on Area.ParentArea means nothing cascades on its own.
    private static List<int> DescendantAreaIds(List<AreaIdParent> areas, int rootId)
    {
        var childrenByParent = areas
            .Where(a => a.ParentAreaId is not null)
            .GroupBy(a => a.ParentAreaId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Id).ToList());

        var result = new List<int> { rootId };
        var queue = new Queue<int>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!childrenByParent.TryGetValue(current, out var children)) continue;
            foreach (var child in children)
            {
                result.Add(child);
                queue.Enqueue(child);
            }
        }

        return result;
    }

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

        // An edit invalidates any prior validation of the area's info, same rule
        // as RoutesController.UpdateRoute — but only when the polygon actually
        // changed, so re-saving it as-is doesn't generate a spurious
        // pending-validation task.
        if (changed)
        {
            area.ValidatedAt = null;
            area.ValidatedByUserId = null;
        }

        await db.SaveChangesAsync();

        return Ok(new AreaDto(
            area.Id,
            area.Name,
            area.Slug,
            area.ParentAreaId,
            area.Polygon.Select(p => new GeoPointDto(p.Lat, p.Lng)).ToList(),
            area.CentroidLat,
            area.CentroidLng,
            area.ValidatedAt,
            await GetValidatedByNameAsync(area.ValidatedByUserId)));
    }

    [Authorize(Policy = Permissions.AreaValidate)]
    [HttpPost("{id:int}/validate")]
    public async Task<ActionResult> ValidateArea(int id)
    {
        var area = await db.Areas.FindAsync(id);
        if (area is null) return NotFound(new { error = "el sector no existe" });

        area.ValidatedAt = DateTimeOffset.UtcNow;
        area.ValidatedByUserId = userManager.GetUserId(User)!;

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
