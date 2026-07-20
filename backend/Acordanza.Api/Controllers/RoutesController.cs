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
    [HttpGet]
    public async Task<ActionResult<List<RouteDto>>> GetRoutes(
        [FromQuery] string? q, [FromQuery] string? crag, [FromQuery] string? type, [FromQuery] string? tag)
    {
        var query = db.Routes.Include(r => r.AddedBy).AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(r => r.Name.Contains(q) || r.Description.Contains(q));
        }
        if (!string.IsNullOrWhiteSpace(crag))
        {
            query = query.Where(r => r.Crag.Contains(crag));
        }
        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(r => r.Type == type);
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(r => r.Tags.Contains(tag));
        }

        var routes = await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new RouteDto(
                r.Id, r.Name, r.Crag, r.Grade, r.GradeSystem, r.Type, r.Description, r.Tags,
                r.FirstAscent, r.AddedBy.UserName!, r.CreatedAt,
                r.Ticks.Count,
                r.Ticks.Count == 0 ? null : r.Ticks.Average(t => (double?)t.Rating)))
            .ToListAsync();

        return Ok(routes);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RouteDetailDto>> GetRoute(int id)
    {
        var route = await db.Routes
            .Include(r => r.AddedBy)
            .Include(r => r.Ticks).ThenInclude(t => t.Climber)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (route is null) return NotFound(new { error = "Not found" });

        var dto = new RouteDetailDto(
            route.Id, route.Name, route.Crag, route.Grade, route.GradeSystem, route.Type,
            route.Description, route.Tags, route.FirstAscent, route.AddedBy.UserName!, route.CreatedAt,
            route.Ticks
                .OrderByDescending(t => t.Date)
                .Select(t => new TickDto(t.Id, t.RouteId, t.Climber.UserName!, t.Date, t.Rating, t.Notes, t.CreatedAt))
                .ToList());

        return Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult> CreateRoute(UpsertRouteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Crag) ||
            string.IsNullOrWhiteSpace(request.Grade))
        {
            return BadRequest(new { error = "name, crag, and grade are required" });
        }

        var userId = userManager.GetUserId(User)!;
        var route = new ClimbingRoute
        {
            Name = request.Name,
            Crag = request.Crag,
            Grade = request.Grade,
            GradeSystem = request.GradeSystem is { Length: > 0 } ? request.GradeSystem : "YDS",
            Type = request.Type is { Length: > 0 } ? request.Type : "sport",
            Description = request.Description ?? "",
            Tags = request.Tags ?? "",
            FirstAscent = request.FirstAscent ?? "",
            AddedByUserId = userId
        };

        db.Routes.Add(route);
        await db.SaveChangesAsync();

        return Ok(new { id = route.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult> UpdateRoute(int id, UpsertRouteRequest request)
    {
        var route = await db.Routes.FindAsync(id);
        if (route is null) return NotFound(new { error = "Not found" });

        route.Name = request.Name;
        route.Crag = request.Crag;
        route.Grade = request.Grade;
        route.GradeSystem = request.GradeSystem ?? route.GradeSystem;
        route.Type = request.Type ?? route.Type;
        route.Description = request.Description ?? "";
        route.Tags = request.Tags ?? "";
        route.FirstAscent = request.FirstAscent ?? "";

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
            return BadRequest(new { error = "date is required" });
        }

        var routeExists = await db.Routes.AnyAsync(r => r.Id == id);
        if (!routeExists) return NotFound(new { error = "Not found" });

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
}
