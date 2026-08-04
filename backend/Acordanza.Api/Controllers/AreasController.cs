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
public class AreasController(AppDbContext db, UserManager<ApplicationUser> userManager) : ControllerBase
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;

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
            .Select(a => new AreaNodeDto(a.Id, a.Name, a.Children.Any()))
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
            .Select(a => new AreaNodeDto(a.Id, a.Name, a.Children.Any()))
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

        var areasToDelete = await db.Areas.Where(a => idsToDelete.Contains(a.Id)).ToListAsync();
        db.Areas.RemoveRange(areasToDelete);
        await db.SaveChangesAsync();

        return NoContent();
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
