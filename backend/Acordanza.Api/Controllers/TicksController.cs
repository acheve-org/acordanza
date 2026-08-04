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
[Route("api/ticks")]
public class TicksController(AppDbContext db, UserManager<ApplicationUser> userManager) : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private sealed record AreaNode(int Id, string Name, string Slug, int? ParentAreaId);

    // The current user's own ticks, newest ascent first — the data source for the
    // "Mis vías" page. Ticks are private: this never exposes another user's rows,
    // unlike GET /routes/{id} which used to (see RoutesController).
    [HttpGet("mine")]
    public async Task<ActionResult<PagedMyTicksResponse>> GetMyTicks(
        [FromQuery] string? q, [FromQuery] int? areaId, [FromQuery] string? gear, [FromQuery] string? tag,
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > MaxPageSize) pageSize = DefaultPageSize;

        var areas = await db.Areas.AsNoTracking()
            .Select(a => new AreaNode(a.Id, a.Name, a.Slug, a.ParentAreaId))
            .ToListAsync();
        var areaById = areas.ToDictionary(a => a.Id);

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
            var ids = DescendantAreaIds(areas, requestedAreaId);
            query = query.Where(t => ids.Contains(t.Route.AreaId));
        }
        if (!string.IsNullOrWhiteSpace(gear))
        {
            query = query.Where(t => t.Route.Gear == gear);
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(t => t.Route.Tags.Contains(tag));
        }

        var totalCount = await query.CountAsync();
        var ticks = await query
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = ticks.Select(t => new MyTickDto(
            t.Id, t.RouteId, t.Route.Name, t.Route.AreaId, areaById[t.Route.AreaId].Name, AreaPath(areaById, t.Route.AreaId),
            t.Route.Grade, t.Route.Orientation, t.Route.Gear, t.Route.Tags,
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

    // Same ancestor walk as RoutesController.DescendantAreaIds — duplicated rather
    // than shared, matching this codebase's flat controller-per-resource style.
    private static List<int> DescendantAreaIds(List<AreaNode> areas, int rootId)
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

    private static string AreaPath(Dictionary<int, AreaNode> areaById, int id)
    {
        var parts = new List<string>();
        int? currentId = id;
        while (currentId is int aid && areaById.TryGetValue(aid, out var area))
        {
            parts.Insert(0, area.Name);
            currentId = area.ParentAreaId;
        }
        return string.Join(" › ", parts);
    }
}
