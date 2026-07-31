using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Acordanza.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/routes")]
public class RoutesController(AppDbContext db, UserManager<ApplicationUser> userManager, BlobStorageService blobStorage)
    : ControllerBase
{
    private static readonly HashSet<string> AllowedOrientations =
        ["north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest"];

    private static readonly HashSet<string> AllowedGear = ["bolted", "removable", "pitons", "aiders"];

    private static readonly HashSet<string> AllowedTopoContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private const long MaxTopoBytes = 15 * 1024 * 1024;
    private static readonly TimeSpan TopoUrlLifetime = TimeSpan.FromMinutes(15);

    private sealed record CragNode(int Id, string Name, string Slug, int? ParentCragId);

    [HttpGet]
    public async Task<ActionResult<List<RouteDto>>> GetRoutes(
        [FromQuery] string? q, [FromQuery] int? cragId, [FromQuery] string? gear, [FromQuery] string? tag,
        [FromQuery] bool? hasTopo)
    {
        var crags = await db.Crags.AsNoTracking()
            .Select(c => new CragNode(c.Id, c.Name, c.Slug, c.ParentCragId))
            .ToListAsync();
        var cragById = crags.ToDictionary(c => c.Id);

        var query = db.Routes
            .Include(r => r.AddedBy)
            .Include(r => r.ValidatedBy)
            .Include(r => r.FirstAscensionists).ThenInclude(o => o.User)
            .Include(r => r.Ticks)
            .Include(r => r.Topos)
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
        if (hasTopo == true)
        {
            query = query.Where(r => r.Topos.Any());
        }

        var routes = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();

        var dtos = routes.Select(r => new RouteDto(
            r.Id, r.Name, r.CragId, cragById[r.CragId].Name, CragPath(cragById, r.CragId),
            r.Grade, r.Orientation, r.Gear, r.Description, r.Tags,
            r.FirstAscensionists.Select(ToFirstAscensionistDto).ToList(),
            r.AddedBy.Name, r.CreatedAt,
            r.Ticks.Count,
            r.Ticks.Count == 0 ? null : r.Ticks.Average(t => (double?)t.Rating),
            r.Topos.Count > 0,
            r.ValidatedAt, r.ValidatedBy?.Name)).ToList();

        return Ok(dtos);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RouteDetailDto>> GetRoute(int id)
    {
        var route = await db.Routes
            .Include(r => r.AddedBy)
            .Include(r => r.ValidatedBy)
            .Include(r => r.FirstAscensionists).ThenInclude(o => o.User)
            .Include(r => r.Ticks).ThenInclude(t => t.Climber)
            .Include(r => r.Topos).ThenInclude(t => t.UploadedBy)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (route is null) return NotFound(new { error = "No encontrado" });

        var crags = await db.Crags.AsNoTracking()
            .Select(c => new CragNode(c.Id, c.Name, c.Slug, c.ParentCragId))
            .ToListAsync();
        var cragById = crags.ToDictionary(c => c.Id);

        var topoDtos = new List<TopoDto>();
        foreach (var topo in route.Topos.OrderBy(t => t.CreatedAt))
        {
            var url = await blobStorage.GetReadSasUrlAsync(topo.BlobName, TopoUrlLifetime);
            topoDtos.Add(new TopoDto(topo.Id, url, topo.FileName, topo.CreatedAt, topo.UploadedBy.Name));
        }

        var dto = new RouteDetailDto(
            route.Id, route.Name, route.CragId, cragById[route.CragId].Name, CragPath(cragById, route.CragId),
            route.Grade, route.Orientation, route.Gear, route.Description, route.Tags,
            route.FirstAscensionists.Select(ToFirstAscensionistDto).ToList(),
            route.AddedBy.Name, route.CreatedAt,
            route.Ticks
                .OrderByDescending(t => t.Date)
                .Select(t => new TickDto(t.Id, t.RouteId, t.Climber.Name, t.Date, t.Rating, t.Notes, t.CreatedAt))
                .ToList(),
            topoDtos,
            route.ValidatedAt, route.ValidatedBy?.Name);

        return Ok(dto);
    }

    [Authorize(Policy = Permissions.RouteCreate)]
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

        var (firstAscensionists, firstAscensionistsError) = await ResolveFirstAscensionistsAsync(request.FirstAscensionistIds);
        if (firstAscensionistsError is not null) return BadRequest(new { error = firstAscensionistsError });

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
            FirstAscensionists = firstAscensionists!
        };

        db.Routes.Add(route);
        await db.SaveChangesAsync();

        return Ok(new { id = route.Id });
    }

    [Authorize(Policy = Permissions.RouteEdit)]
    [HttpPut("{id:int}")]
    public async Task<ActionResult> UpdateRoute(int id, UpsertRouteRequest request)
    {
        var route = await db.Routes.Include(r => r.FirstAscensionists).FirstOrDefaultAsync(r => r.Id == id);
        if (route is null) return NotFound(new { error = "No encontrado" });

        if (!await db.Crags.AnyAsync(c => c.Id == request.CragId))
        {
            return BadRequest(new { error = "el sector no existe" });
        }

        var orientationError = ValidateOrientation(request.Orientation);
        if (orientationError is not null) return BadRequest(new { error = orientationError });

        var gearError = ValidateGear(request.Gear);
        if (gearError is not null) return BadRequest(new { error = gearError });

        var (firstAscensionists, firstAscensionistsError) = await ResolveFirstAscensionistsAsync(request.FirstAscensionistIds);
        if (firstAscensionistsError is not null) return BadRequest(new { error = firstAscensionistsError });

        route.Name = request.Name;
        route.CragId = request.CragId;
        route.Grade = request.Grade;
        route.Orientation = request.Orientation;
        route.Gear = request.Gear;
        route.Description = request.Description ?? "";
        route.Tags = request.Tags ?? "";

        route.FirstAscensionists.Clear();
        route.FirstAscensionists.AddRange(firstAscensionists!);

        // An edit invalidates any prior validation of the route's info.
        route.ValidatedAt = null;
        route.ValidatedByUserId = null;

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [Authorize(Policy = Permissions.RouteValidate)]
    [HttpPost("{id:int}/validate")]
    public async Task<ActionResult> ValidateRoute(int id)
    {
        var route = await db.Routes.FindAsync(id);
        if (route is null) return NotFound(new { error = "No encontrado" });

        var userId = userManager.GetUserId(User)!;
        route.ValidatedAt = DateTimeOffset.UtcNow;
        route.ValidatedByUserId = userId;

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [Authorize(Policy = Permissions.RouteDelete)]
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteRoute(int id)
    {
        var route = await db.Routes.Include(r => r.Topos).FirstOrDefaultAsync(r => r.Id == id);
        if (route is not null)
        {
            // The Topo rows cascade-delete with the route (see AppDbContext),
            // but that's DB-only — their blobs won't clean themselves up, so
            // delete those explicitly first while BlobName is still known.
            foreach (var topo in route.Topos)
            {
                await blobStorage.DeleteAsync(topo.BlobName);
            }

            db.Routes.Remove(route);
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }

    [HttpPost("{id:int}/topos")]
    [RequestSizeLimit(MaxTopoBytes)]
    public async Task<ActionResult<TopoDto>> UploadTopo(int id, IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "la imagen es obligatoria" });
        }
        if (!AllowedTopoContentTypes.Contains(file.ContentType))
        {
            return BadRequest(new { error = "el formato de la imagen no es válido (usa JPEG, PNG o WebP)" });
        }
        if (file.Length > MaxTopoBytes)
        {
            return BadRequest(new { error = "la imagen es demasiado grande (máximo 15 MB)" });
        }

        var route = await db.Routes.FirstOrDefaultAsync(r => r.Id == id);
        if (route is null) return NotFound(new { error = "No encontrado" });

        var crags = await db.Crags.AsNoTracking()
            .Select(c => new CragNode(c.Id, c.Name, c.Slug, c.ParentCragId))
            .ToListAsync();
        var cragById = crags.ToDictionary(c => c.Id);

        var currentUser = await userManager.GetUserAsync(User);
        if (currentUser is null) return Unauthorized();

        var topo = new Topo
        {
            RouteId = route.Id,
            BlobName = "",
            ContentType = file.ContentType,
            FileName = file.FileName,
            UploadedByUserId = currentUser.Id
        };
        db.Topos.Add(topo);
        await db.SaveChangesAsync();

        // The blob name needs the DB-generated Id, so it's assigned in a
        // second save — same two-phase pattern as CragsController's Slug.
        var extension = Path.GetExtension(file.FileName);
        topo.BlobName = $"{CragSlugPath(cragById, route.CragId)}/route-{route.Id}/{topo.Id}{extension}";
        await db.SaveChangesAsync();

        try
        {
            await using var stream = file.OpenReadStream();
            await blobStorage.UploadAsync(topo.BlobName, stream, topo.ContentType);
        }
        catch
        {
            // The blob never landed (or landed only partially) — don't leave
            // a Topo row pointing at content that doesn't exist, which would
            // otherwise mark the route as having a topo and render a broken
            // image in the detail view.
            db.Topos.Remove(topo);
            await db.SaveChangesAsync();
            throw;
        }

        var url = await blobStorage.GetReadSasUrlAsync(topo.BlobName, TopoUrlLifetime);
        return Ok(new TopoDto(topo.Id, url, topo.FileName, topo.CreatedAt, currentUser.Name));
    }

    [Authorize(Policy = Permissions.TickCreate)]
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

    private async Task<(List<FirstAscensionist>? FirstAscensionists, string? Error)> ResolveFirstAscensionistsAsync(List<int>? firstAscensionistIds)
    {
        if (firstAscensionistIds is not { Count: > 0 }) return ([], null);

        var distinctIds = firstAscensionistIds.Distinct().ToList();
        var firstAscensionists = await db.FirstAscensionists.Where(o => distinctIds.Contains(o.Id)).ToListAsync();
        if (firstAscensionists.Count != distinctIds.Count)
        {
            return (null, "algún aperturista no existe");
        }

        return (firstAscensionists, null);
    }

    private static FirstAscensionistDto ToFirstAscensionistDto(FirstAscensionist o) => new(o.Id, o.Name, o.UserId, o.User?.Name);

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

    // Same ancestor walk as CragPath, but joining Slug (not Name) with "/" —
    // this is the blob-name prefix topo images for routes under this crag
    // are stored under.
    private static string CragSlugPath(Dictionary<int, CragNode> cragById, int id)
    {
        var parts = new List<string>();
        int? currentId = id;
        while (currentId is int cid && cragById.TryGetValue(cid, out var crag))
        {
            parts.Insert(0, crag.Slug);
            currentId = crag.ParentCragId;
        }
        return string.Join("/", parts);
    }
}
