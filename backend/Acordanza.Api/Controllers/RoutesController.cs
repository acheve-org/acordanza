using System.Data;
using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Acordanza.Api.Services;
using Azure;
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

    private sealed record AreaNode(int Id, string Name, string Slug, int? ParentAreaId);

    [HttpGet]
    public async Task<ActionResult<List<RouteDto>>> GetRoutes(
        [FromQuery] string? q, [FromQuery] int? areaId, [FromQuery] string? gear, [FromQuery] string? tag,
        [FromQuery] bool? hasTopo, [FromQuery] bool exactArea = false)
    {
        var areas = await db.Areas.AsNoTracking()
            .Select(a => new AreaNode(a.Id, a.Name, a.Slug, a.ParentAreaId))
            .ToListAsync();
        var areaById = areas.ToDictionary(a => a.Id);

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
            List<int> ids = exactArea ? [requestedAreaId] : DescendantAreaIds(areas, requestedAreaId);
            query = query.Where(r => ids.Contains(r.AreaId));
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

        List<ClimbingRoute> routes;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
        {
            routes = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
            await transaction.CommitAsync();
        }

        var canValidateArea = await BuildCanValidateAreaCheckAsync(areaById);

        var userId = userManager.GetUserId(User)!;
        var tickedRouteIds = (await db.Ticks
            .Where(t => t.ClimberUserId == userId)
            .Select(t => t.RouteId)
            .ToListAsync()).ToHashSet();

        var dtos = routes.Select(r => new RouteDto(
            r.Id, r.Name, r.AreaId, areaById[r.AreaId].Name, AreaPath(areaById, r.AreaId),
            r.Grade, r.Orientation, r.Gear, r.Description, r.Tags,
            r.FirstAscensionists.Select(ToFirstAscensionistDto).ToList(),
            r.AddedBy.Name, r.CreatedAt,
            tickedRouteIds.Contains(r.Id),
            r.Topos.Count > 0,
            r.ValidatedAt, r.ValidatedBy?.Name,
            canValidateArea(r.AreaId))).ToList();

        return Ok(dtos);
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

        var areas = await db.Areas.AsNoTracking()
            .Select(a => new AreaNode(a.Id, a.Name, a.Slug, a.ParentAreaId))
            .ToListAsync();
        var areaById = areas.ToDictionary(a => a.Id);

        var topoDtos = new List<TopoDto>();
        foreach (var topo in route.Topos.OrderBy(t => t.CreatedAt))
        {
            var url = await blobStorage.GetReadSasUrlAsync(topo.BlobName, TopoUrlLifetime);
            topoDtos.Add(new TopoDto(topo.Id, url, topo.FileName, topo.CreatedAt, topo.UploadedBy.Name));
        }

        var canValidateArea = await BuildCanValidateAreaCheckAsync(areaById);

        var userId = userManager.GetUserId(User)!;
        var hasTick = await db.Ticks.AnyAsync(t => t.RouteId == route.Id && t.ClimberUserId == userId);

        var dto = new RouteDetailDto(
            route.Id, route.Name, route.AreaId, areaById[route.AreaId].Name, AreaPath(areaById, route.AreaId),
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
        if (!User.HasClaim("permission", Permissions.RouteValidate))
        {
            var areas = await db.Areas.AsNoTracking()
                .Select(a => new AreaNode(a.Id, a.Name, a.Slug, a.ParentAreaId))
                .ToListAsync();
            var topAreaId = TopLevelAreaId(areas.ToDictionary(a => a.Id), route.AreaId);
            var isExpert = await db.AreaExperts.AnyAsync(
                e => e.UserId == userId && e.Status == AreaExpertStatus.Approved && e.AreaId == topAreaId);
            if (!isExpert) return Forbid();
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

        var areas = await db.Areas.AsNoTracking()
            .Select(a => new AreaNode(a.Id, a.Name, a.Slug, a.ParentAreaId))
            .ToListAsync();
        var areaById = areas.ToDictionary(a => a.Id);

        var currentUser = await userManager.GetUserAsync(User);
        if (currentUser is null) return Unauthorized();

        // Topos are named after the route's slug plus a numeric suffix, e.g.
        // "some-route-01.jpg". The next number is the highest one actually in
        // use under this exact area+slug prefix, not just this route's topo
        // count: (a) two different routes can slugify to the same name, so the
        // prefix (not RouteId) is what has to stay collision-free, and (b) if
        // an earlier topo was deleted, a plain count could reissue a number a
        // surviving topo with a higher number still owns.
        var extension = Path.GetExtension(file.FileName);
        var blobPrefix = $"{AreaSlugPath(areaById, route.AreaId)}/{Slugify.ToSlug(route.Name)}-";
        var usedBlobNames = await db.Topos.AsNoTracking()
            .Where(t => t.BlobName.StartsWith(blobPrefix))
            .Select(t => t.BlobName)
            .ToListAsync();
        var nextNumber = HighestToposNumber(usedBlobNames, blobPrefix) + 1;

        // A concurrent upload could grab the same number between the query above and
        // the write below — upload conditionally (fails instead of overwriting if the
        // name is already taken) and retry under the next number when that happens.
        const int maxAttempts = 5;
        string? blobName = null;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var candidate = $"{blobPrefix}{nextNumber + attempt:D2}{extension}";
            try
            {
                await using var stream = file.OpenReadStream();
                await blobStorage.UploadAsync(candidate, stream, file.ContentType, overwrite: false);
                blobName = candidate;
                break;
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // Name taken by a racing upload — try the next number.
            }
        }

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
        await db.SaveChangesAsync();

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

    // Builds a per-route check for the RouteDto.CanValidate flag: users with the
    // route.validate permission can validate anything; otherwise, only routes
    // under a top-level area the current user is an approved expert of.
    private async Task<Func<int, bool>> BuildCanValidateAreaCheckAsync(Dictionary<int, AreaNode> areaById)
    {
        if (User.HasClaim("permission", Permissions.RouteValidate))
        {
            return _ => true;
        }

        var userId = userManager.GetUserId(User)!;
        var expertAreaIds = await db.AreaExperts
            .Where(e => e.UserId == userId && e.Status == AreaExpertStatus.Approved)
            .Select(e => e.AreaId)
            .ToListAsync();

        if (expertAreaIds.Count == 0)
        {
            return _ => false;
        }

        return areaId => expertAreaIds.Contains(TopLevelAreaId(areaById, areaId));
    }

    private static int TopLevelAreaId(Dictionary<int, AreaNode> areaById, int id)
    {
        var result = id;
        int? currentId = id;
        while (currentId is int aid && areaById.TryGetValue(aid, out var area))
        {
            result = aid;
            currentId = area.ParentAreaId;
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

    // Same ancestor walk as AreaPath, but joining Slug (not Name) with "/" —
    // this is the blob-name prefix topo images for routes under this area
    // are stored under.
    private static string AreaSlugPath(Dictionary<int, AreaNode> areaById, int id)
    {
        var parts = new List<string>();
        int? currentId = id;
        while (currentId is int aid && areaById.TryGetValue(aid, out var area))
        {
            parts.Insert(0, area.Slug);
            currentId = area.ParentAreaId;
        }
        return string.Join("/", parts);
    }

    // Parses the trailing "-{number}" out of each blob name (after the shared prefix,
    // before the extension) and returns the highest one found, or 0 if none/none parse.
    private static int HighestToposNumber(List<string> blobNames, string prefix)
    {
        var highest = 0;
        foreach (var blobName in blobNames)
        {
            var numberPart = Path.GetFileNameWithoutExtension(blobName[prefix.Length..]);
            if (int.TryParse(numberPart, out var number) && number > highest)
            {
                highest = number;
            }
        }
        return highest;
    }
}
