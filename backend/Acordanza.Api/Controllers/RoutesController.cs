using System.Data;
using Azure;
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
    AreaExpertAuthorizer areaExpertAuthorizer,
    RouteEnrichmentQueue enrichmentQueue) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<RouteDto>>> GetRoutes(
        [FromQuery] string? q, [FromQuery] int? areaId, [FromQuery] string? grade, [FromQuery] string? aidGrade,
        [FromQuery] string[]? orientation,
        [FromQuery] string? gear, [FromQuery] bool? hasTopo,
        [FromQuery] int? firstAscensionistId, [FromQuery] bool exactArea = false, [FromQuery] bool openedByMe = false)
    {
        var areas = await areaHierarchy.GetAsync();
        var userId = userManager.GetUserId(User)!;

        var query = db.Routes
            .Include(r => r.AddedBy)
            .Include(r => r.DecidedBy)
            .Include(r => r.FirstAscensionists).ThenInclude(o => o.User)
            .Include(r => r.Topos)
            .Include(r => r.Links)
            .AsSplitQuery()
            .AsQueryable();

        if (openedByMe)
        {
            // "Mis aperturas" (Mi acordanza): these are by definition the caller's own
            // openings, possibly created by someone else and still pending — nothing to
            // hide from them, so this bypasses the visibility filter below rather than
            // combining with it.
            query = query.Where(r => r.FirstAscensionists.Any(fa => fa.UserId == userId));
        }
        else
        {
            query = query.Where(ValidationVisibility.Routes(userId));
        }

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
            // "none" is the "Sin grado" dropdown option (Filters.tsx). Safe as a sentinel
            // rather than a separate query param because no legal French grade is
            // spellable as "none" — and a separate boolean param would be a second
            // control that can contradict this one (grade=6a AND ungraded=true has no
            // meaning).
            query = grade == "none"
                ? query.Where(r => r.Grade == null)
                : query.Where(r => r.Grade == grade);
        }
        if (!string.IsNullOrWhiteSpace(aidGrade))
        {
            query = query.Where(r => r.AidGrade == aidGrade);
        }
        if (orientation is { Length: > 0 })
        {
            if (orientation.FirstOrDefault(o => !RouteWrite.AllowedOrientations.Contains(o)) is not null)
                return BadRequest(new { error = "la orientación no es válida" });
            query = query.Where(r => r.Orientation != null && orientation.Contains(r.Orientation));
        }
        if (!string.IsNullOrWhiteSpace(gear))
        {
            query = query.Where(r => r.Gear == gear);
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

        var tickedRouteIds = (await db.Ticks
            .Where(t => t.ClimberUserId == userId)
            .Select(t => t.RouteId)
            .ToListAsync()).ToHashSet();

        var dtos = routes.Select(r => new RouteDto(
            r.Id, r.Name, r.Slug, r.AreaId, areas[r.AreaId].Name, areas.NamePath(r.AreaId), areas.SlugPath(r.AreaId),
            r.Grade, r.AidGrade, r.Orientation, r.Gear, r.Description,
            r.Length, r.ObligatoryGrade, r.RecommendedGear, r.OptionalGear,
            r.Links.Select(l => new RouteLinkDto(l.Url, l.Source.ToString().ToLowerInvariant())).ToList(),
            r.FirstAscensionists.Select(ToFirstAscensionistDto).ToList(),
            r.AddedBy.Name, r.CreatedAt,
            tickedRouteIds.Contains(r.Id),
            r.Topos.Count > 0,
            r.ValidationStatus.ToString().ToLowerInvariant(), r.DecidedAt, r.DecidedBy?.Name, r.RejectionReason,
            canValidateArea(r.AreaId))).ToList();

        return Ok(dtos);
    }

    // Grades are validated on write against the French scale (Models/GradeValidator), so
    // the search filter offers the ones actually in use rather than all 54 legal ones.
    [HttpGet("grades")]
    public async Task<ActionResult<List<string>>> GetGrades()
    {
        var userId = userManager.GetUserId(User)!;
        var grades = await db.Routes.AsNoTracking()
            .Where(ValidationVisibility.Routes(userId))
            .Where(r => r.Grade != null)
            .Select(r => r.Grade!)
            .Distinct()
            .OrderBy(g => g)
            .ToListAsync();

        return Ok(grades);
    }

    // Mirrors GetGrades exactly, over AidGrade instead — no "sin grado artificial" option
    // (nearly every route in the log has no aid grade, so that filter would select almost
    // everything and answer nothing).
    [HttpGet("aid-grades")]
    public async Task<ActionResult<List<string>>> GetAidGrades()
    {
        var userId = userManager.GetUserId(User)!;
        var aidGrades = await db.Routes.AsNoTracking()
            .Where(ValidationVisibility.Routes(userId))
            .Where(r => r.AidGrade != null)
            .Select(r => r.AidGrade!)
            .Distinct()
            .OrderBy(g => g)
            .ToListAsync();

        return Ok(aidGrades);
    }

    // The review queue's view of not-yet-decided routes. No policy attribute — narrowed
    // per-caller by AreaExpertAuthorizer instead, same guard ValidateRoute/RejectRoute use:
    // a caller with neither route.validate nor an approved area expertise gets [] (the
    // check degrades to `_ => false`), same as the bell's current client-side empty state.
    [HttpGet("pending")]
    public async Task<ActionResult<List<RoutePendingDto>>> GetPendingRoutes()
    {
        var canValidateArea = await areaExpertAuthorizer.BuildAreaCheckAsync(User, Permissions.RouteValidate);

        var areas = await areaHierarchy.GetAsync();
        var pending = await db.Routes.AsNoTracking()
            .Include(r => r.AddedBy)
            .Where(r => r.ValidationStatus == ReviewStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        return Ok(pending
            .Where(r => canValidateArea(r.AreaId))
            .Select(r => new RoutePendingDto(
                r.Id, r.Name, r.Slug, areas.NamePath(r.AreaId), areas.SlugPath(r.AreaId), r.Grade, r.CreatedAt, r.AddedBy.Name))
            .ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RouteDetailDto>> GetRoute(int id)
    {
        ClimbingRoute? route;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
        {
            route = await db.Routes
                .Include(r => r.AddedBy)
                .Include(r => r.DecidedBy)
                .Include(r => r.FirstAscensionists).ThenInclude(o => o.User)
                .Include(r => r.Topos).ThenInclude(t => t.UploadedBy)
                .Include(r => r.Links)
                .AsSplitQuery()
                .FirstOrDefaultAsync(r => r.Id == id);
            await transaction.CommitAsync();
        }

        if (route is null) return NotFound(new { error = "No encontrado" });

        return Ok(await BuildRouteDetailDtoAsync(route));
    }

    // Resolves a route by its full shareable path — {area-slug-path}/{route-slug}, e.g.
    // "riglos/pena-amarillo/fisura-del-diablo" — splitting the trailing segment off as
    // the route slug candidate and the rest as the area path (AreaHierarchy.ResolveSlugPath,
    // the same walk AreasController's own by-slug-path action uses). Alongside, not
    // replacing, GET /routes/{id} — RouteFormPage's edit route keeps using the numeric id.
    [HttpGet("by-slug-path/{*path}")]
    public async Task<ActionResult<RouteDetailDto>> GetRouteBySlugPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return NotFound(new { error = "No encontrado" });
        }

        var routeSlug = segments[^1];
        var areaSegments = segments[..^1];

        var hierarchy = await areaHierarchy.GetAsync();
        var area = hierarchy.ResolveSlugPath(areaSegments);
        if (area is null)
        {
            return NotFound(new { error = "No encontrado" });
        }

        ClimbingRoute? route;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
        {
            route = await db.Routes
                .Include(r => r.AddedBy)
                .Include(r => r.DecidedBy)
                .Include(r => r.FirstAscensionists).ThenInclude(o => o.User)
                .Include(r => r.Topos).ThenInclude(t => t.UploadedBy)
                .Include(r => r.Links)
                .AsSplitQuery()
                .FirstOrDefaultAsync(r => r.AreaId == area.Id && r.Slug == routeSlug);
            await transaction.CommitAsync();
        }

        if (route is null) return NotFound(new { error = "No encontrado" });

        return Ok(await BuildRouteDetailDtoAsync(route));
    }

    private async Task<RouteDetailDto> BuildRouteDetailDtoAsync(ClimbingRoute route)
    {
        var areas = await areaHierarchy.GetAsync();
        var userId = userManager.GetUserId(User)!;

        // Same rule ToposController.DeleteTopo enforces, resolved once for the whole list.
        var canEditArea = await areaExpertAuthorizer.BuildAreaCheckAsync(User, Permissions.RouteEdit);
        var canDeleteAnyTopo = canEditArea(route.AreaId);

        var topoDtos = new List<TopoDto>();
        foreach (var topo in route.Topos.OrderBy(t => t.CreatedAt))
        {
            var url = await blobStorage.GetReadSasUrlAsync(topo.BlobName, MediaUploadService.ImageUrlLifetime);
            topoDtos.Add(new TopoDto(
                topo.Id, url, topo.FileName, topo.CreatedAt, topo.UploadedBy.Name,
                canDeleteAnyTopo || topo.UploadedByUserId == userId));
        }

        var canValidateArea = await areaExpertAuthorizer.BuildAreaCheckAsync(User, Permissions.RouteValidate);

        var hasTick = await db.Ticks.AnyAsync(t => t.RouteId == route.Id && t.ClimberUserId == userId);

        return new RouteDetailDto(
            route.Id, route.Name, route.Slug, route.AreaId, areas[route.AreaId].Name, areas.NamePath(route.AreaId),
            areas.SlugPath(route.AreaId),
            route.Grade, route.AidGrade, route.Orientation, route.Gear, route.Description,
            route.Length, route.ObligatoryGrade, route.RecommendedGear, route.OptionalGear,
            route.Links.Select(l => new RouteLinkDto(l.Url, l.Source.ToString().ToLowerInvariant())).ToList(),
            route.FirstAscensionists.Select(ToFirstAscensionistDto).ToList(),
            route.AddedBy.Name, route.CreatedAt,
            hasTick,
            topoDtos,
            route.ValidationStatus.ToString().ToLowerInvariant(), route.DecidedAt, route.DecidedBy?.Name, route.RejectionReason,
            canValidateArea(route.AreaId), route.EnrichedAt);
    }

    [Authorize(Policy = Permissions.RouteCreate)]
    [HttpPost]
    public async Task<ActionResult> CreateRoute(UpsertRouteRequest request)
    {
        var (values, error) = await RouteWrite.TryBuildAsync(db, request, requireAreaId: true);
        if (error is not null) return BadRequest(new { error });

        var userId = userManager.GetUserId(User)!;
        var slug = await GenerateUniqueRouteSlugAsync(request.AreaId, request.Name);
        var route = new ClimbingRoute
        {
            Name = request.Name,
            Slug = slug,
            AreaId = request.AreaId,
            Grade = values!.Grade,
            AidGrade = values.AidGrade,
            Orientation = values.Orientation,
            Gear = values.Gear,
            Description = values.Description,
            Length = values.Length,
            ObligatoryGrade = values.ObligatoryGrade,
            RecommendedGear = values.RecommendedGear,
            OptionalGear = values.OptionalGear,
            Links = values.Links.Select(u => new Link { Url = u, Source = LinkSource.Human }).ToList(),
            AddedByUserId = userId,
            FirstAscensionists = values.FirstAscensionists
        };

        // Same rule AreasController.CreateArea and FirstAscensionistsController.CreateFirstAscensionist
        // apply — but via AreaExpertAuthorizer rather than a bare claim check, since route validation
        // rights are area-scoped: an approved area expert creating a route inside their own area can
        // validate it too, and shouldn't have to file a task for themselves.
        if (await areaExpertAuthorizer.CanActOnAreaAsync(User, Permissions.RouteValidate, route.AreaId))
        {
            Validation.MarkValidated(route, userId);
        }

        db.Routes.Add(route);
        await db.SaveChangesAsync();

        return Ok(new { id = route.Id });
    }

    [Authorize(Policy = Permissions.RouteEdit)]
    [HttpPut("{id:int}")]
    public async Task<ActionResult> UpdateRoute(int id, UpsertRouteRequest request)
    {
        var route = await db.Routes
            .Include(r => r.FirstAscensionists)
            .Include(r => r.Links)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (route is null) return NotFound(new { error = "No encontrado" });

        var userId = userManager.GetUserId(User)!;

        var (values, error) = await RouteWrite.TryBuildAsync(db, request);
        if (error is not null) return BadRequest(new { error });

        var newFirstAscensionistIds = values!.FirstAscensionists.Select(fa => fa.Id).ToHashSet();
        var newLinkUrls = values.Links.ToHashSet();
        var changed = route.Name != request.Name ||
            route.AreaId != request.AreaId ||
            route.Grade != values.Grade ||
            route.AidGrade != values.AidGrade ||
            (route.Orientation ?? "") != (values.Orientation ?? "") ||
            (route.Gear ?? "") != (values.Gear ?? "") ||
            route.Description != values.Description ||
            route.Length != values.Length ||
            (route.ObligatoryGrade ?? "") != (values.ObligatoryGrade ?? "") ||
            (route.RecommendedGear ?? "") != (values.RecommendedGear ?? "") ||
            (route.OptionalGear ?? "") != (values.OptionalGear ?? "") ||
            !route.Links.Select(l => l.Url).ToHashSet().SetEquals(newLinkUrls) ||
            !route.FirstAscensionists.Select(fa => fa.Id).ToHashSet().SetEquals(newFirstAscensionistIds);

        // Regenerated against the *new* area's sibling set whenever the name or the area
        // itself changes — a route moved into an area that happens to already have a
        // same-named route still needs the "-2" suffix. A no-op re-save (excludeId guard
        // inside GenerateUniqueRouteSlugAsync) keeps the existing slug.
        if (route.Name != request.Name || route.AreaId != request.AreaId)
        {
            route.Slug = await GenerateUniqueRouteSlugAsync(request.AreaId, request.Name, route.Id);
        }

        route.Name = request.Name;
        route.AreaId = request.AreaId;
        route.Grade = values.Grade;
        route.AidGrade = values.AidGrade;
        route.Orientation = values.Orientation;
        route.Gear = values.Gear;
        route.Description = values.Description;
        route.Length = values.Length;
        route.ObligatoryGrade = values.ObligatoryGrade;
        route.RecommendedGear = values.RecommendedGear;
        route.OptionalGear = values.OptionalGear;

        route.Links.Clear();
        route.Links.AddRange(values.Links.Select(u => new Link { Url = u, Source = LinkSource.Human }));

        route.FirstAscensionists.Clear();
        route.FirstAscensionists.AddRange(values.FirstAscensionists);

        // Same rule as CreateRoute: an editor who could validate this route outright (role
        // permission or approved area expert) shouldn't have their own edit demote it to
        // Pending just to immediately re-approve it via a second call.
        if (changed)
        {
            if (await areaExpertAuthorizer.CanActOnAreaAsync(User, Permissions.RouteValidate, route.AreaId))
            {
                Validation.MarkValidated(route, userId);
            }
            else
            {
                Validation.InvalidateIfChanged(route, changed);
            }
        }

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

        Validation.MarkValidated(route, userId);

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [Authorize]
    [HttpPost("{id:int}/reject")]
    public async Task<ActionResult> RejectRoute(int id, RejectValidationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "el motivo es obligatorio" });
        }

        var route = await db.Routes.FindAsync(id);
        if (route is null) return NotFound(new { error = "No encontrado" });

        var userId = userManager.GetUserId(User)!;

        // Same guard as ValidateRoute — an area expert can reject within their area too.
        if (!await areaExpertAuthorizer.CanActOnAreaAsync(User, Permissions.RouteValidate, route.AreaId))
        {
            return Forbid();
        }

        Validation.MarkRejected(route, userId, request.Reason.Trim());

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
        var validationError = ImageValidation.ValidateImage(file);
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

        var blobName = await mediaUpload.UploadNumberedAsync(blobPrefix, extension, usedBlobNames, file.OpenReadStream(), file.ContentType);
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
        // The uploader can always delete what they just uploaded, so this needs no check.
        return Ok(new TopoDto(topo.Id, url, topo.FileName, topo.CreatedAt, currentUser.Name, CanDelete: true));
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

    // Manually triggered, per route — never on create/edit (see plan 18). Admin-only
    // (Permissions.RouteEnrich), because it spends money: a different question from
    // editing a route, and route.edit is also granted dynamically to every linked
    // first ascensionist, which would hand the spend button to a set of people that
    // grows on its own.
    [Authorize(Policy = Permissions.RouteEnrich)]
    [HttpPost("{id:int}/enrich")]
    public async Task<ActionResult> EnrichRoute(int id, [FromQuery] bool force = false)
    {
        var route = await db.Routes.FindAsync(id);
        if (route is null) return NotFound(new { error = "No encontrado" });

        if (route.EnrichedAt is not null && !force)
        {
            return Conflict(new { error = "Esta vía ya se ha enriquecido" });
        }

        try
        {
            await enrichmentQueue.EnqueueAsync(id, force);
        }
        catch (RequestFailedException)
        {
            // Nothing else happened in this request — a failed enqueue is not
            // best-effort-and-swallow, unlike the (now-removed) create-time hook.
            return StatusCode(503, new { error = "No se pudo iniciar la búsqueda, inténtalo de nuevo" });
        }

        // A JSON body (not truly empty) so the frontend's shared request() helper, which
        // always parses a response body, doesn't need a 202-specific exception.
        return Accepted(new { ok = true });
    }

    private static FirstAscensionistDto ToFirstAscensionistDto(FirstAscensionist o) => new(o.Id, o.Name, o.UserId, o.User?.Name);

    // Thin wrapper over Core's RouteSlugs, where the loop itself lives now that the topo
    // import worker creates routes too (plan 30 §4).
    private Task<string> GenerateUniqueRouteSlugAsync(int areaId, string name, int? excludeId = null) =>
        RouteSlugs.GenerateUniqueAsync(db, areaId, name, excludeId);
}
