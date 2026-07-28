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
[Route("api/routes")]
public class RoutesController(AppDbContext db, UserManager<ApplicationUser> userManager) : ControllerBase
{
    private static readonly HashSet<string> AllowedOrientations =
        ["north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest"];

    private static readonly HashSet<string> AllowedGear = ["bolted", "removable", "pitons", "aiders"];

    private sealed record CragNode(int Id, string Name, int? ParentCragId);

    [HttpGet]
    public async Task<ActionResult<List<RouteDto>>> GetRoutes(
        [FromQuery] string? q, [FromQuery] int? cragId, [FromQuery] string? gear, [FromQuery] string? tag)
    {
        var crags = await db.Crags.AsNoTracking()
            .Select(c => new CragNode(c.Id, c.Name, c.ParentCragId))
            .ToListAsync();
        var cragById = crags.ToDictionary(c => c.Id);

        var query = db.Routes
            .Include(r => r.AddedBy)
            .Include(r => r.Openers).ThenInclude(o => o.User)
            .Include(r => r.Ticks)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(r => r.Name.Contains(q) || r.Description.Contains(q));
        }
        if (cragId is int requestedCragId)
        {
            var ids = DescendantCragIds(crags, requestedCragId);
            query = query.Where(r => ids.Contains(r.CragId));
        }
        if (!string.IsNullOrWhiteSpace(gear))
        {
            query = query.Where(r => r.Gear == gear);
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(r => r.Tags.Contains(tag));
        }

        var routes = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();

        var dtos = routes.Select(r => new RouteDto(
            r.Id, r.Name, r.CragId, cragById[r.CragId].Name, CragPath(cragById, r.CragId),
            r.Grade, r.Orientation, r.Gear, r.Description, r.Tags,
            r.Openers.Select(ToOpenerDto).ToList(),
            r.AddedBy.UserName!, r.CreatedAt,
            r.Ticks.Count,
            r.Ticks.Count == 0 ? null : r.Ticks.Average(t => (double?)t.Rating))).ToList();

        return Ok(dtos);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RouteDetailDto>> GetRoute(int id)
    {
        var route = await db.Routes
            .Include(r => r.AddedBy)
            .Include(r => r.Openers).ThenInclude(o => o.User)
            .Include(r => r.Ticks).ThenInclude(t => t.Climber)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (route is null) return NotFound(new { error = "No encontrado" });

        var crags = await db.Crags.AsNoTracking()
            .Select(c => new CragNode(c.Id, c.Name, c.ParentCragId))
            .ToListAsync();
        var cragById = crags.ToDictionary(c => c.Id);

        var dto = new RouteDetailDto(
            route.Id, route.Name, route.CragId, cragById[route.CragId].Name, CragPath(cragById, route.CragId),
            route.Grade, route.Orientation, route.Gear, route.Description, route.Tags,
            route.Openers.Select(ToOpenerDto).ToList(),
            route.AddedBy.UserName!, route.CreatedAt,
            route.Ticks
                .OrderByDescending(t => t.Date)
                .Select(t => new TickDto(t.Id, t.RouteId, t.Climber.UserName!, t.Date, t.Rating, t.Notes, t.CreatedAt))
                .ToList());

        return Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult> CreateRoute(UpsertRouteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Grade))
        {
            return BadRequest(new { error = "el nombre y el grado son obligatorios" });
        }

        if (!await db.Crags.AnyAsync(c => c.Id == request.CragId))
        {
            return BadRequest(new { error = "el sector no existe" });
        }

        var orientationError = ValidateOrientation(request.Orientation);
        if (orientationError is not null) return BadRequest(new { error = orientationError });

        var gearError = ValidateGear(request.Gear);
        if (gearError is not null) return BadRequest(new { error = gearError });

        var (openers, openersError) = await ResolveOpenersAsync(request.OpenerIds);
        if (openersError is not null) return BadRequest(new { error = openersError });

        var userId = userManager.GetUserId(User)!;
        var route = new ClimbingRoute
        {
            Name = request.Name,
            CragId = request.CragId,
            Grade = request.Grade,
            Orientation = request.Orientation,
            Gear = request.Gear,
            Description = request.Description ?? "",
            Tags = request.Tags ?? "",
            AddedByUserId = userId,
            Openers = openers!
        };

        db.Routes.Add(route);
        await db.SaveChangesAsync();

        return Ok(new { id = route.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult> UpdateRoute(int id, UpsertRouteRequest request)
    {
        var route = await db.Routes.Include(r => r.Openers).FirstOrDefaultAsync(r => r.Id == id);
        if (route is null) return NotFound(new { error = "No encontrado" });

        if (!await db.Crags.AnyAsync(c => c.Id == request.CragId))
        {
            return BadRequest(new { error = "el sector no existe" });
        }

        var orientationError = ValidateOrientation(request.Orientation);
        if (orientationError is not null) return BadRequest(new { error = orientationError });

        var gearError = ValidateGear(request.Gear);
        if (gearError is not null) return BadRequest(new { error = gearError });

        var (openers, openersError) = await ResolveOpenersAsync(request.OpenerIds);
        if (openersError is not null) return BadRequest(new { error = openersError });

        route.Name = request.Name;
        route.CragId = request.CragId;
        route.Grade = request.Grade;
        route.Orientation = request.Orientation;
        route.Gear = request.Gear;
        route.Description = request.Description ?? "";
        route.Tags = request.Tags ?? "";

        route.Openers.Clear();
        route.Openers.AddRange(openers!);

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteRoute(int id)
    {
        var route = await db.Routes.FindAsync(id);
        if (route is not null)
        {
            db.Routes.Remove(route);
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }

    [HttpPost("{id:int}/ticks")]
    public async Task<ActionResult> CreateTick(int id, CreateTickRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Date))
        {
            return BadRequest(new { error = "la fecha es obligatoria" });
        }

        var routeExists = await db.Routes.AnyAsync(r => r.Id == id);
        if (!routeExists) return NotFound(new { error = "No encontrado" });

        var userId = userManager.GetUserId(User)!;
        var tick = new Tick
        {
            RouteId = id,
            ClimberUserId = userId,
            Date = request.Date,
            Rating = request.Rating,
            Notes = request.Notes ?? ""
        };

        db.Ticks.Add(tick);
        await db.SaveChangesAsync();

        return Ok(new { id = tick.Id });
    }

    private static string? ValidateOrientation(string? orientation) =>
        orientation is not null && !AllowedOrientations.Contains(orientation)
            ? "la orientación no es válida"
            : null;

    private static string? ValidateGear(string? gear) =>
        gear is not null && !AllowedGear.Contains(gear)
            ? "el tipo de equipamiento no es válido"
            : null;

    private async Task<(List<RouteOpener>? Openers, string? Error)> ResolveOpenersAsync(List<int>? openerIds)
    {
        if (openerIds is not { Count: > 0 }) return ([], null);

        var distinctIds = openerIds.Distinct().ToList();
        var openers = await db.RouteOpeners.Where(o => distinctIds.Contains(o.Id)).ToListAsync();
        if (openers.Count != distinctIds.Count)
        {
            return (null, "algún aperturista no existe");
        }

        return (openers, null);
    }

    private static RouteOpenerDto ToOpenerDto(RouteOpener o) => new(o.Id, o.Name, o.UserId, o.User?.UserName);

    private static List<int> DescendantCragIds(List<CragNode> crags, int rootId)
    {
        var childrenByParent = crags
            .Where(c => c.ParentCragId is not null)
            .GroupBy(c => c.ParentCragId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());

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

    private static string CragPath(Dictionary<int, CragNode> cragById, int id)
    {
        var parts = new List<string>();
        int? currentId = id;
        while (currentId is int cid && cragById.TryGetValue(cid, out var crag))
        {
            parts.Insert(0, crag.Name);
            currentId = crag.ParentCragId;
        }
        return string.Join(" › ", parts);
    }
}
