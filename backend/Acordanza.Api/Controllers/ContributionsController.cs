using Acordanza.Core.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Acordanza.Core.Models;
using Acordanza.Api.Services;
using Acordanza.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Controllers;

// A read model over three tables (routes/areas/first ascensionists the current user
// created) — belongs to none of their per-resource controllers. Backs the "Contribuciones"
// page under "Mi acordanza" (issue #125).
//
// One paginated action per kind (issue #149) rather than one payload with all three: the
// page shows a single tab at a time, so the other two kinds are never fetched until their
// tab is opened. Every action scopes to the caller — no id is ever accepted from the
// client — which is why the paths carry no "mine" segment.
[Authorize]
[ApiController]
[Route("api/contributions")]
public class ContributionsController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    AreaHierarchyService areaHierarchy) : ControllerBase
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;

    [HttpGet("routes")]
    public async Task<ActionResult<PagedContributionsResponse>> GetRoutes(
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize)
    {
        (page, pageSize) = PageRequest.Clamp(page, pageSize, DefaultPageSize, MaxPageSize);
        var userId = userManager.GetUserId(User)!;

        var query = db.Routes.AsNoTracking().Where(r => r.AddedByUserId == userId);
        var totalCount = await query.CountAsync();
        // CreatedAt alone isn't a total order (rows created in the same tick tie, and
        // Postgres may return tied rows in any order per query), so every action here
        // tie-breaks on Id — without it a tied row could land on two pages and another on none.
        var rows = await query
            .Include(r => r.DecidedBy)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        // Projected in memory, not in SQL: NamePath is a lookup over AreaHierarchyService's
        // in-memory snapshot and can't be translated. Paging still runs in the database.
        var areas = await areaHierarchy.GetAsync();
        var items = rows
            .Select(r => new ContributionDto(
                r.Id, r.Name, areas.NamePath(r.AreaId), r.CreatedAt,
                r.ValidationStatus.ToString().ToLowerInvariant(), r.DecidedAt, r.DecidedBy?.Name, r.RejectionReason))
            .ToList();

        return Ok(new PagedContributionsResponse(items, totalCount, page, pageSize));
    }

    [HttpGet("areas")]
    public async Task<ActionResult<PagedContributionsResponse>> GetAreas(
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize)
    {
        (page, pageSize) = PageRequest.Clamp(page, pageSize, DefaultPageSize, MaxPageSize);
        var userId = userManager.GetUserId(User)!;

        var query = db.Areas.AsNoTracking().Where(a => a.AddedByUserId == userId);
        var totalCount = await query.CountAsync();
        var rows = await query
            .Include(a => a.DecidedBy)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        var areas = await areaHierarchy.GetAsync();
        var items = rows
            .Select(a => new ContributionDto(
                a.Id, a.Name, areas.NamePath(a.Id), a.CreatedAt,
                a.ValidationStatus.ToString().ToLowerInvariant(), a.DecidedAt, a.DecidedBy?.Name, a.RejectionReason))
            .ToList();

        return Ok(new PagedContributionsResponse(items, totalCount, page, pageSize));
    }

    // No area hierarchy snapshot here: openers carry no Detail, so opening only the
    // Aperturistas tab never materialises the area tree.
    [HttpGet("first-ascensionists")]
    public async Task<ActionResult<PagedContributionsResponse>> GetFirstAscensionists(
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize)
    {
        (page, pageSize) = PageRequest.Clamp(page, pageSize, DefaultPageSize, MaxPageSize);
        var userId = userManager.GetUserId(User)!;

        var query = db.FirstAscensionists.AsNoTracking().Where(f => f.AddedByUserId == userId);
        var totalCount = await query.CountAsync();
        var rows = await query
            .Include(f => f.DecidedBy)
            .OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        var items = rows
            .Select(f => new ContributionDto(
                f.Id, f.Name, null, f.CreatedAt,
                f.ValidationStatus.ToString().ToLowerInvariant(), f.DecidedAt, f.DecidedBy?.Name, f.RejectionReason))
            .ToList();

        return Ok(new PagedContributionsResponse(items, totalCount, page, pageSize));
    }
}
