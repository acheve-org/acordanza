using System.Data;
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
public class RoutesController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    BlobStorageService blobStorage,
    MediaUploadService mediaUpload,
    AreaHierarchyService areaHierarchy,
    AreaExpertAuthorizer areaExpertAuthorizer) : ControllerBase
{
    private static readonly HashSet<string> AllowedOrientations =
        ["north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest"];

    private static readonly HashSet<string> AllowedGear = ["bolted", "removable", "pitons", "aiders"];

    [HttpGet]
    public async Task<ActionResult<List<RouteDto>>> GetRoutes(
        [FromQuery] string? q, [FromQuery] int? areaId, [FromQuery] string? grade, [FromQuery] string? orientation,
        [FromQuery] string? gear, [FromQuery] string? tag, [FromQuery] bool? hasTopo,
        [FromQuery] int? firstAscensionistId, [FromQuery] bool exactArea = false)
    {
        var areas = await areaHierarchy.GetAsync();

        var query = db.Routes
            .Include(r => r.AddedBy)
            .Include(r => r.ValidatedBy)
            .Include(r => r.FirstAscensionists).ThenInclude(o => o.User)
            .Include(r => r.Topos)
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(r => r.Name.Contains(q) || r.Description.Contains(q));
        }
        if (areaId is int requestedAreaId)
        {
            // The area detail page (issue #105) wants only this exact area's own routes —
            // its child areas' routes get their own section there — so it opts out of the
            // usual subtree expansion every other caller (filters, area picker) relies on.
            List<int> ids = exactArea ? [requestedAreaId] : areas.DescendantIds(requestedAreaId);
            query = query.Where(r => ids.Contains(r.AreaId));
        }
        if (!string.IsNullOrWhiteSpace(grade))
        {
            query = query.Where(r => r.Grade == grade);
        }
        if (!string.IsNullOrWhiteSpace(orientation))
        {
            query = query.Where(r => r.Orientation == orientation);
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
        // Backs the "Sigue" section's per-aperturista route list (issue #108).
        if (firstAscensionistId is int openerId)
        {
            query = query.Where(r => r.FirstAscensionists.Any(o => o.Id == openerId));
        }

        List<ClimbingRoute> routes;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
        {
            routes = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
            await transaction.CommitAsync();
        }

        var canValidateArea = await areaExpertAuthorizer.BuildAreaCheckAsync(User, Permissions.RouteValidate);

        var userId = userManager.GetUserId(User)!;
        var tickedRouteIds = (await db.Ticks
            .Where(t => t.ClimberUserId == userId)
            .Select(t => t.RouteId)
            .ToListAsync()).ToHashSet();

        var dtos = routes.Select(r => new RouteDto(
            r.Id, r.Name, r.AreaId, areas[r.AreaId].Name, areas.NamePath(r.AreaId),
            r.Grade, r.Orientation, r.Gear, r.Description, r.Tags,
            r.FirstAscensionists.Select(ToFirstAscensionistDto).ToList(),
            r.AddedBy.Name, r.CreatedAt,
            tickedRouteIds.Contains(r.Id),
            r.Topos.Count > 0,
            r.ValidatedAt, r.ValidatedBy?.Name,
            canValidateArea(r.AreaId))).ToList();

        return Ok(dtos);
    }

    // Grades are free text (see Models/ClimbingRoute), so the search filter offers the
    // ones actually in use rather than a hardcoded scale.
    [HttpGet("grades")]
    public async Task<ActionResult<List<string>>> GetGrades()
    {
        var grades = await db.Routes.AsNoTracking()
            .Select(r => r.Grade)
            .Distinct()
            .OrderBy(g => g)
            .ToListAsync();

        return Ok(grades);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RouteDetailDto>> GetRoute(int id)
    {
        ClimbingRoute? route;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
        {
            route = await db.Routes
                .Include(r => r.AddedBy)
                .Include(r => r.ValidatedBy)
                .Include(r => r.FirstAscensionists).ThenInclude(o => o.User)
                .Include(r => r.Topos).ThenInclude(t => t.UploadedBy)
                .AsSplitQuery()
                .FirstOrDefaultAsync(r => r.Id == id);
            await transaction.CommitAsync();
        }

        if (route is null) return NotFound(new { error = "No encontrado" });

        var areas = await areaHierarchy.GetAsync();

        var topoDtos = new List<TopoDto>();
        foreach (var topo in route.Topos.OrderBy(t => t.CreatedAt))
        {
            var url = await blobStorage.GetReadSasUrlAsync(topo.BlobName, MediaUploadService.ImageUrlLifetime);
            topoDtos.Add(new TopoDto(topo.Id, url, topo.FileName, topo.CreatedAt, topo.UploadedBy.Name));
        }

        var canValidateArea = await areaExpertAuthorizer.BuildAreaCheckAsync(User, Permissions.RouteValidate);

        var userId = userManager.GetUserId(User)!;
        var hasTick = await db.Ticks.AnyAsync(t => t.RouteId == route.Id && t.ClimberUserId == userId);

        var dto = new RouteDetailDto(
            route.Id, route.Name, route.AreaId, areas[route.AreaId].Name, areas.NamePath(route.AreaId),
            route.Grade, route.Orientation, route.Gear, route.Description, route.Tags,
            route.FirstAscensionists.Select(ToFirstAscensionistDto).ToList(),
            route.AddedBy.Name, route.CreatedAt,
            hasTick,
            topoDtos,
            route.ValidatedAt, route.ValidatedBy?.Name,
            canValidateArea(route.AreaId));

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

        if (!await db.Areas.AnyAsync(a => a.Id == request.AreaId))
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
            AreaId = request.AreaId,
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

        if (!await db.Areas.AnyAsync(a => a.Id == request.AreaId))
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
        route.AreaId = request.AreaId;
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

    [Authorize]
    [HttpPost("{id:int}/validate")]
    public async Task<ActionResult> ValidateRoute(int id)
    {
        var route = await db.Routes.FindAsync(id);
        if (route is null) return NotFound(new { error = "No encontrado" });

        var userId = userManager.GetUserId(User)!;

        // Route validation is normally role-gated (route.validate), but an area
        // expert can also validate routes within the top-level area they're an
        // approved expert of (and any of its subareas) even without that role.
        if (!await areaExpertAuthorizer.CanActOnAreaAsync(User, Permissions.RouteValidate, route.AreaId))
        {
            return Forbid();
        }

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
    [RequestSizeLimit(MediaUploadService.MaxImageBytes)]
    public async Task<ActionResult<TopoDto>> UploadTopo(int id, IFormFile? file)
    {
        var validationError = mediaUpload.ValidateImage(file);
        if (validationError is not null) return BadRequest(new { error = validationError.Message });

        var route = await db.Routes.FirstOrDefaultAsync(r => r.Id == id);
        if (route is null) return NotFound(new { error = "No encontrado" });

        var areas = await areaHierarchy.GetAsync();

        var currentUser = await userManager.GetUserAsync(User);
        if (currentUser is null) return Unauthorized();

        // Topos are named after the route's slug plus a numeric suffix, e.g.
        // "some-route-01.jpg" — two different routes can slugify to the same name, so
        // the prefix (not RouteId) is what has to stay collision-free.
        var extension = Path.GetExtension(file!.FileName);
        var blobPrefix = $"{areas.SlugPath(route.AreaId)}/{Slugify.ToSlug(route.Name)}-";
        var usedBlobNames = await db.Topos.AsNoTracking()
            .Where(t => t.BlobName.StartsWith(blobPrefix))
            .Select(t => t.BlobName)
            .ToListAsync();

        var blobName = await mediaUpload.UploadNumberedAsync(blobPrefix, extension, usedBlobNames, file);
        if (blobName is null)
        {
            return Conflict(new { error = "no se pudo subir la imagen, inténtalo de nuevo" });
        }

        var topo = new Topo
        {
            RouteId = route.Id,
            BlobName = blobName,
            ContentType = file.ContentType,
            FileName = file.FileName,
            UploadedByUserId = currentUser.Id
        };
        db.Topos.Add(topo);
        await mediaUpload.SaveOrCleanupAsync(blobName, () => db.SaveChangesAsync());

        var url = await blobStorage.GetReadSasUrlAsync(topo.BlobName, MediaUploadService.ImageUrlLifetime);
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
}
