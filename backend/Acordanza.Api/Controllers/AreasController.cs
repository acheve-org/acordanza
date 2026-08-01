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
    public async Task<ActionResult<List<AreaDto>>> GetAreas()
    {
        var areas = await db.Areas.AsNoTracking()
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
