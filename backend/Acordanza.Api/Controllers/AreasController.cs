using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Acordanza.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/areas")]
public class AreasController(AppDbContext db) : ControllerBase
{
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
                a.CentroidLng))
            .ToListAsync();

        return Ok(areas);
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

        var area = new Area { Name = request.Name.Trim(), ParentAreaId = request.ParentAreaId };
        db.Areas.Add(area);
        await db.SaveChangesAsync();

        // The slug needs the DB-generated Id to guarantee uniqueness, so it's
        // set in a second save rather than computed before the insert.
        area.Slug = $"{Slugify.ToSlug(area.Name)}-{area.Id}";
        await db.SaveChangesAsync();

        return Ok(new AreaDto(area.Id, area.Name, area.ParentAreaId, [], null, null));
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

        area.Name = request.Name.Trim();
        area.ParentAreaId = request.ParentAreaId;
        await db.SaveChangesAsync();

        return Ok(new AreaDto(
            area.Id,
            area.Name,
            area.ParentAreaId,
            area.Polygon.Select(p => new GeoPointDto(p.Lat, p.Lng)).ToList(),
            area.CentroidLat,
            area.CentroidLng));
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

        if (await db.Areas.AnyAsync(a => a.ParentAreaId == id))
        {
            return BadRequest(new { error = "el sector tiene subsectores; elimínalos primero" });
        }

        if (await db.Routes.AnyAsync(r => r.AreaId == id))
        {
            return BadRequest(new { error = "el sector tiene vías asociadas; muévelas o elimínalas primero" });
        }

        db.Areas.Remove(area);
        await db.SaveChangesAsync();

        return NoContent();
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

        area.Polygon = request.Polygon.Select(p => new GeoPoint { Lat = p.Lat, Lng = p.Lng }).ToList();

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

        await db.SaveChangesAsync();

        return Ok(new AreaDto(
            area.Id,
            area.Name,
            area.ParentAreaId,
            area.Polygon.Select(p => new GeoPointDto(p.Lat, p.Lng)).ToList(),
            area.CentroidLat,
            area.CentroidLng));
    }
}
