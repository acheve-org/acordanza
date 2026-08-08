using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Acordanza.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Controllers;

// A read model over three tables (routes/areas/first ascensionists the current user
// created) — belongs to none of their per-resource controllers. Backs the "Contribuciones"
// page under "Mi acordanza" (issue #125).
[Authorize]
[ApiController]
[Route("api/contributions")]
public class ContributionsController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    AreaHierarchyService areaHierarchy) : ControllerBase
{
    [HttpGet("mine")]
    public async Task<ActionResult<MyContributionsResponse>> GetMine()
    {
        var userId = userManager.GetUserId(User)!;
        var areas = await areaHierarchy.GetAsync();

        var routes = await db.Routes.AsNoTracking()
            .Include(r => r.DecidedBy)
            .Where(r => r.AddedByUserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        var routeDtos = routes
            .Select(r => new ContributionDto(
                r.Id, r.Name, $"{areas.NamePath(r.AreaId)} · {r.Grade}", r.CreatedAt,
                r.ValidationStatus.ToString().ToLowerInvariant(), r.DecidedAt, r.DecidedBy?.Name, r.RejectionReason))
            .ToList();

        var areaRows = await db.Areas.AsNoTracking()
            .Include(a => a.DecidedBy)
            .Where(a => a.AddedByUserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
        var areaDtos = areaRows
            .Select(a => new ContributionDto(
                a.Id, a.Name, areas.NamePath(a.Id), a.CreatedAt,
                a.ValidationStatus.ToString().ToLowerInvariant(), a.DecidedAt, a.DecidedBy?.Name, a.RejectionReason))
            .ToList();

        var firstAscensionists = await db.FirstAscensionists.AsNoTracking()
            .Include(f => f.DecidedBy)
            .Where(f => f.AddedByUserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();
        var firstAscensionistDtos = firstAscensionists
            .Select(f => new ContributionDto(
                f.Id, f.Name, null, f.CreatedAt,
                f.ValidationStatus.ToString().ToLowerInvariant(), f.DecidedAt, f.DecidedBy?.Name, f.RejectionReason))
            .ToList();

        return Ok(new MyContributionsResponse(routeDtos, areaDtos, firstAscensionistDtos));
    }
}
