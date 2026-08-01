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
            .Select(a => new AreaDto(a.Id, a.Name, a.ParentAreaId))
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

        return Ok(new AreaDto(area.Id, area.Name, area.ParentAreaId));
    }
}
