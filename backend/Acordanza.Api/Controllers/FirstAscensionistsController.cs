using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/first-ascensionists")]
public class FirstAscensionistsController(AppDbContext db, UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<FirstAscensionistDto>>> GetFirstAscensionists([FromQuery] string? q)
    {
        var query = db.FirstAscensionists.AsNoTracking().Include(o => o.User).AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(o => o.Name.Contains(q));
        }

        var firstAscensionists = await query
            .OrderBy(o => o.Name)
            .Select(o => new FirstAscensionistDto(o.Id, o.Name, o.UserId, o.User != null ? o.User.Name : null))
            .ToListAsync();

        return Ok(firstAscensionists);
    }

    // Backs the "Sigue" section (issue #108): the same list, plus how much each one has
    // opened and where. The openings are read from the route side because the
    // ClimbingRoute <-> FirstAscensionist many-to-many has no inverse navigation.
    [HttpGet("profiles")]
    public async Task<ActionResult<List<FirstAscensionistProfileDto>>> GetFirstAscensionistProfiles(
        [FromQuery] string? q)
    {
        var query = db.FirstAscensionists.AsNoTracking().Include(o => o.User).AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(o => o.Name.Contains(q));
        }

        var firstAscensionists = await query.OrderBy(o => o.Name).ToListAsync();
        var openingsById = await OpeningsByFirstAscensionistAsync();

        return Ok(firstAscensionists.Select(o => ToProfileDto(o, openingsById)).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<FirstAscensionistProfileDto>> GetFirstAscensionist(int id)
    {
        var firstAscensionist = await db.FirstAscensionists.AsNoTracking()
            .Include(o => o.User)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (firstAscensionist is null) return NotFound(new { error = "No encontrado" });

        return Ok(ToProfileDto(firstAscensionist, await OpeningsByFirstAscensionistAsync(id)));
    }

    private async Task<Dictionary<int, List<string>>> OpeningsByFirstAscensionistAsync(int? firstAscensionistId = null)
    {
        var query = db.Routes.AsNoTracking().AsQueryable();
        if (firstAscensionistId is int id)
        {
            query = query.Where(r => r.FirstAscensionists.Any(o => o.Id == id));
        }

        var openings = await query
            .SelectMany(r => r.FirstAscensionists.Select(o => new { FirstAscensionistId = o.Id, AreaName = r.Area.Name }))
            .ToListAsync();

        return openings
            .GroupBy(o => o.FirstAscensionistId)
            .ToDictionary(g => g.Key, g => g.Select(o => o.AreaName).ToList());
    }

    private static FirstAscensionistProfileDto ToProfileDto(
        FirstAscensionist firstAscensionist, Dictionary<int, List<string>> openingsById)
    {
        var areaNames = openingsById.TryGetValue(firstAscensionist.Id, out var openings) ? openings : [];
        return new FirstAscensionistProfileDto(
            firstAscensionist.Id,
            firstAscensionist.Name,
            firstAscensionist.UserId,
            firstAscensionist.User?.Name,
            areaNames.Count,
            areaNames.Distinct().OrderBy(name => name).ToList());
    }

    // Creates an unlinked FirstAscensionist record (e.g. a historic route opener
    // with no account yet). Linking one to a user account always goes through
    // FirstAscensionistLinksController's request/approve flow now, even for
    // admins/moderators, so who-linked-what-when is always tracked.
    [Authorize(Policy = Permissions.FirstAscensionistCreate)]
    [HttpPost]
    public async Task<ActionResult<FirstAscensionistDto>> CreateFirstAscensionist(CreateFirstAscensionistRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "el nombre es obligatorio" });
        }

        var firstAscensionist = new FirstAscensionist { Name = request.Name.Trim() };
        db.FirstAscensionists.Add(firstAscensionist);
        await db.SaveChangesAsync();

        return Ok(new FirstAscensionistDto(firstAscensionist.Id, firstAscensionist.Name, null, null));
    }

    [Authorize(Policy = Permissions.FirstAscensionistEdit)]
    [HttpPut("{id}")]
    public async Task<ActionResult<FirstAscensionistDto>> UpdateFirstAscensionist(int id, UpdateFirstAscensionistRequest request)
    {
        var firstAscensionist = await db.FirstAscensionists.Include(o => o.User).FirstOrDefaultAsync(o => o.Id == id);
        if (firstAscensionist is null)
        {
            return NotFound(new { error = "el aperturista no existe" });
        }

        // first-ascensionist.edit is granted both to moderators/admins (any record) and, dynamically,
        // to a user linked to their own FirstAscensionist record (see DynamicPermissions) — since both
        // carry the same claim, a linked user editing someone else's record has to be blocked here,
        // same "ownership scoped to a query, not a claim" pattern
        // AreasController.BuildCanManageAreaMediaCheckAsync uses for AreaExperts.
        var isPrivileged = User.IsInRole(Roles.Moderator) || User.IsInRole(Roles.Admin);
        if (!isPrivileged && firstAscensionist.UserId != userManager.GetUserId(User))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "el nombre es obligatorio" });
        }

        firstAscensionist.Name = request.Name.Trim();
        await db.SaveChangesAsync();

        return Ok(new FirstAscensionistDto(
            firstAscensionist.Id, firstAscensionist.Name, firstAscensionist.UserId, firstAscensionist.User?.Name));
    }

    [Authorize(Policy = Permissions.FirstAscensionistDelete)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteFirstAscensionist(int id)
    {
        if (!await db.FirstAscensionists.AnyAsync(f => f.Id == id))
        {
            return NotFound(new { error = "el aperturista no existe" });
        }

        // ClimbingRoute<->FirstAscensionist is many-to-many with a Cascade FK on the join
        // table (see AppDbContext), so nothing would stop the delete at the DB level —
        // block it explicitly here instead, same pattern as AreasController.DeleteArea. Doing
        // the "has routes" check and the delete as a single statement (rather than a separate
        // AnyAsync then Remove) closes the window where a route create/edit could link this
        // first ascensionist in between, which would otherwise get silently cascade-deleted.
        var deleted = await db.FirstAscensionists
            .Where(f => f.Id == id && !db.Routes.Any(r => r.FirstAscensionists.Any(fa => fa.Id == id)))
            .ExecuteDeleteAsync();

        if (deleted == 0)
        {
            if (!await db.FirstAscensionists.AnyAsync(f => f.Id == id))
            {
                return NotFound(new { error = "el aperturista no existe" });
            }
            return BadRequest(new { error = "el aperturista tiene vías asociadas; quítalo de esas vías primero" });
        }

        return NoContent();
    }
}
