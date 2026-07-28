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
[Route("api/crags")]
public class CragsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CragDto>>> GetCrags()
    {
        var crags = await db.Crags.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CragDto(c.Id, c.Name, c.ParentCragId))
            .ToListAsync();

        return Ok(crags);
    }

    [HttpPost]
    public async Task<ActionResult<CragDto>> CreateCrag(CreateCragRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "el nombre es obligatorio" });
        }

        if (request.ParentCragId is int parentId && !await db.Crags.AnyAsync(c => c.Id == parentId))
        {
            return BadRequest(new { error = "el sector padre no existe" });
        }

        var crag = new Crag { Name = request.Name.Trim(), ParentCragId = request.ParentCragId };
        db.Crags.Add(crag);
        await db.SaveChangesAsync();

        // The slug needs the DB-generated Id to guarantee uniqueness, so it's
        // set in a second save rather than computed before the insert.
        crag.Slug = $"{Slugify.ToSlug(crag.Name)}-{crag.Id}";
        await db.SaveChangesAsync();

        return Ok(new CragDto(crag.Id, crag.Name, crag.ParentCragId));
    }
}
