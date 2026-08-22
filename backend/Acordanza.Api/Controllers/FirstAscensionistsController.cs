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
using Npgsql;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/first-ascensionists")]
public class FirstAscensionistsController(AppDbContext db, UserManager<ApplicationUser> userManager) : ControllerBase
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;
    private const int NameMaxLength = 200;

    // unlinkedOnly narrows to records with no linked account — RequestFirstAscensionistLinkModal's
    // candidate list (a claimant can only request a still-unlinked opener), filtered here rather
    // than client-side so TotalCount/paging reflect what's actually offered, not the raw table.
    [HttpGet]
    public async Task<ActionResult<PagedFirstAscensionistsResponse>> GetFirstAscensionists(
        [FromQuery] string? q, [FromQuery] bool includeUnvalidated = false, [FromQuery] bool unlinkedOnly = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize)
    {
        if (includeUnvalidated && !User.HasClaim("permission", Permissions.FirstAscensionistValidate))
        {
            return Forbid();
        }

        if (page < 1) page = 1;
        if (pageSize is < 1 or > MaxPageSize) pageSize = DefaultPageSize;

        var query = db.FirstAscensionists.AsNoTracking().Include(o => o.User).AsQueryable();
        if (!includeUnvalidated)
        {
            query = query.Where(ValidationVisibility.FirstAscensionists(userManager.GetUserId(User)!));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(o => o.Name.Contains(q));
        }

        if (unlinkedOnly)
        {
            query = query.Where(o => o.UserId == null);
        }

        var totalCount = await query.CountAsync();
        var firstAscensionists = await query
            .OrderBy(o => o.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new FirstAscensionistDto(o.Id, o.Name, o.UserId, o.User != null ? o.User.Name : null))
            .ToListAsync();

        return Ok(new PagedFirstAscensionistsResponse(firstAscensionists, totalCount, page, pageSize));
    }

    // Backs the "Sigue" section (issue #108): the same list, plus how much each one has
    // opened and where. The openings are read from the route side because the
    // ClimbingRoute <-> FirstAscensionist many-to-many has no inverse navigation.
    [HttpGet("profiles")]
    public async Task<ActionResult<List<FirstAscensionistProfileDto>>> GetFirstAscensionistProfiles(
        [FromQuery] string? q)
    {
        var query = db.FirstAscensionists.AsNoTracking().Include(o => o.User).Include(o => o.DecidedBy)
            .Where(ValidationVisibility.FirstAscensionists(userManager.GetUserId(User)!))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(o => o.Name.Contains(q));
        }

        var firstAscensionists = await query.OrderBy(o => o.Name).ToListAsync();
        var openingsById = await OpeningsByFirstAscensionistAsync();

        return Ok(firstAscensionists.Select(o => ToProfileDto(o, openingsById)).ToList());
    }

    // by-slug/ rather than a bare {slug} so it can't shadow the literal "profiles"/"pending"
    // segments above — same shape as AreasController's by-slug-path.
    [HttpGet("by-slug/{slug}")]
    public async Task<ActionResult<FirstAscensionistProfileDto>> GetFirstAscensionistBySlug(string slug)
    {
        var firstAscensionist = await db.FirstAscensionists.AsNoTracking()
            .Include(o => o.User)
            .Include(o => o.DecidedBy)
            .FirstOrDefaultAsync(o => o.Slug == slug);

        if (firstAscensionist is null) return NotFound(new { error = "el aperturista no existe" });

        return Ok(ToProfileDto(firstAscensionist, await OpeningsByFirstAscensionistAsync(firstAscensionist.Id)));
    }

    // The review queue's view of not-yet-decided first ascensionists — the first
    // real use of first-ascensionist.validate outside the two permission-declaration
    // lists (RolePermissions/Permissions), see plan 10.
    [Authorize(Policy = Permissions.FirstAscensionistValidate)]
    [HttpGet("pending")]
    public async Task<ActionResult<List<FirstAscensionistPendingDto>>> GetPendingFirstAscensionists()
    {
        var pending = await db.FirstAscensionists.AsNoTracking()
            .Include(o => o.AddedBy)
            .Where(o => o.ValidationStatus == ReviewStatus.Pending)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();

        return Ok(pending
            .Select(o => new FirstAscensionistPendingDto(o.Id, o.Name, o.CreatedAt, o.AddedBy?.Name))
            .ToList());
    }

    // First-ascensionist names aren't unique (unlike Area, which enforces per-parent
    // uniqueness), so this "-2"/"-3" suffix loop is the primary disambiguator here, not
    // just a diacritics edge case — two real climbers named "José García" get
    // jose-garcia and jose-garcia-2. excludeId lets an unchanged name keep its slug on
    // update instead of colliding with itself.
    private async Task<string> GenerateUniqueSlugAsync(string name, int? excludeId = null)
    {
        var baseSlug = Slugify.ToSlug(name, "aperturista");
        var slug = baseSlug;
        var suffix = 2;
        while (await db.FirstAscensionists.AnyAsync(o => o.Slug == slug && o.Id != excludeId))
        {
            slug = $"{baseSlug}-{suffix}";
            suffix++;
        }
        return slug;
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
            firstAscensionist.Slug,
            firstAscensionist.UserId,
            firstAscensionist.User?.Name,
            areaNames.Count,
            areaNames.Distinct().OrderBy(name => name).ToList(),
            firstAscensionist.ValidationStatus.ToString().ToLowerInvariant(),
            firstAscensionist.DecidedAt,
            firstAscensionist.DecidedBy?.Name,
            firstAscensionist.RejectionReason);
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
        if (request.Name.Length > NameMaxLength)
        {
            return BadRequest(new { error = "el nombre es demasiado largo" });
        }

        var userId = userManager.GetUserId(User)!;
        var name = request.Name.Trim();
        var slug = await GenerateUniqueSlugAsync(name);
        var firstAscensionist = new FirstAscensionist { Name = name, Slug = slug, AddedByUserId = userId };

        // Same rule AreasController.CreateArea applies for areas: a creator who can also
        // validate first ascensionists (moderator/admin) has their new record
        // auto-validated, so they don't file a review task for themselves.
        if (User.HasClaim("permission", Permissions.FirstAscensionistValidate))
        {
            Validation.MarkValidated(firstAscensionist, userId);
        }

        db.FirstAscensionists.Add(firstAscensionist);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The uniqueness loop above is only a friendly pre-check — this catches the rare
            // case of two concurrent creates racing past it for the same slug.
            return Conflict(new { error = "ya existe un aperturista con esa dirección" });
        }

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
        // AreaExpertAuthorizer implements for AreaExperts.
        var isPrivileged = User.IsInRole(Roles.Moderator) || User.IsInRole(Roles.Admin);
        if (!isPrivileged && firstAscensionist.UserId != userManager.GetUserId(User))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "el nombre es obligatorio" });
        }
        if (request.Name.Length > NameMaxLength)
        {
            return BadRequest(new { error = "el nombre es demasiado largo" });
        }

        var newName = request.Name.Trim();
        var changed = newName != firstAscensionist.Name;

        firstAscensionist.Name = newName;
        if (changed)
        {
            firstAscensionist.Slug = await GenerateUniqueSlugAsync(newName, firstAscensionist.Id);
        }

        // Same rule as CreateFirstAscensionist: an editor who could validate this record outright
        // shouldn't have their own edit demote it to Pending just to immediately re-approve it.
        if (changed)
        {
            if (User.HasClaim("permission", Permissions.FirstAscensionistValidate))
            {
                Validation.MarkValidated(firstAscensionist, userManager.GetUserId(User)!);
            }
            else
            {
                Validation.InvalidateIfChanged(firstAscensionist, changed);
            }
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { error = "ya existe un aperturista con esa dirección" });
        }

        return Ok(new FirstAscensionistDto(
            firstAscensionist.Id, firstAscensionist.Name, firstAscensionist.UserId, firstAscensionist.User?.Name));
    }

    [Authorize(Policy = Permissions.FirstAscensionistValidate)]
    [HttpPost("{id:int}/validate")]
    public async Task<ActionResult> ValidateFirstAscensionist(int id)
    {
        var firstAscensionist = await db.FirstAscensionists.FindAsync(id);
        if (firstAscensionist is null) return NotFound(new { error = "el aperturista no existe" });

        Validation.MarkValidated(firstAscensionist, userManager.GetUserId(User)!);

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [Authorize(Policy = Permissions.FirstAscensionistValidate)]
    [HttpPost("{id:int}/reject")]
    public async Task<ActionResult> RejectFirstAscensionist(int id, RejectValidationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "el motivo es obligatorio" });
        }

        var firstAscensionist = await db.FirstAscensionists.FindAsync(id);
        if (firstAscensionist is null) return NotFound(new { error = "el aperturista no existe" });

        Validation.MarkRejected(firstAscensionist, userManager.GetUserId(User)!, request.Reason.Trim());

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
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
