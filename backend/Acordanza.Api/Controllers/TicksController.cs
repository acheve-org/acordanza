using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Acordanza.Api.Dtos;
using Acordanza.Core.Models;
using Acordanza.Api.Services;
using Acordanza.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Acordanza.Api.Dtos.RouteEnumParsing;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/ticks")]
public class TicksController(AppDbContext db, UserManager<ApplicationUser> userManager, AreaHierarchyService areaHierarchy)
    : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    // The current user's own ticks, newest ascent first — the data source for the
    // "Mis vías" page. Ticks are private: this never exposes another user's rows,
    // unlike GET /routes/{id} which used to (see RoutesController).
    [HttpGet("mine")]
    public async Task<ActionResult<PagedMyTicksResponse>> GetMyTicks(
        [FromQuery] string? q, [FromQuery] int? areaId, [FromQuery] string? gear,
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > MaxPageSize) pageSize = DefaultPageSize;

        var areas = await areaHierarchy.GetAsync();

        var userId = userManager.GetUserId(User)!;
        var query = db.Ticks
            .Include(t => t.Route)
            .Where(t => t.ClimberUserId == userId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(t => t.Route.Name.Contains(q) || t.Route.Description.Contains(q));
        }
        if (areaId is int requestedAreaId)
        {
            var ids = areas.DescendantIds(requestedAreaId);
            query = query.Where(t => ids.Contains(t.Route.AreaId));
        }
        if (!string.IsNullOrWhiteSpace(gear))
        {
            query = query.Where(t => t.Route.Gear == gear);
        }

        var totalCount = await query.CountAsync();
        var ticks = await query
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = ticks.Select(t => new MyTickDto(
            t.Id, t.RouteId, t.Route.Name, t.Route.Slug, t.Route.AreaId, areas[t.Route.AreaId].Name, areas.NamePath(t.Route.AreaId),
            areas.SlugPath(t.Route.AreaId), t.Route.Grade, t.Route.AidGrade,
            ParseOrientation(t.Route.Orientation), ParseGear(t.Route.Gear), t.Route.Length,
            t.Date, t.Rating, t.Notes, t.CreatedAt)).ToList();

        return Ok(new PagedMyTicksResponse(items, totalCount, page, pageSize));
    }

    // tick.delete is broadly granted (every role has it, see RolePermissions),
    // so the policy alone doesn't stop one user from deleting another's tick
    // by guessing its (sequential, unauthenticated-visible-nowhere-but-still
    // guessable) id — ticks are private, so this also scopes to the caller,
    // same pattern as PasskeysController's self-service delete.
    [Authorize(Policy = Permissions.TickDelete)]
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteTick(int id)
    {
        var userId = userManager.GetUserId(User)!;
        var tick = await db.Ticks.FirstOrDefaultAsync(t => t.Id == id && t.ClimberUserId == userId);
        if (tick is not null)
        {
            db.Ticks.Remove(tick);
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }
}
