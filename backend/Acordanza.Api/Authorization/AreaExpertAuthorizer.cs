using System.Security.Claims;
using Acordanza.Api.Data;
using Acordanza.Api.Models;
using Acordanza.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Authorization;

// The single home of the "permission claim OR approved area expert" rule: a user
// holding `permission` can act on any area; otherwise they can act only on areas
// whose top-level ancestor they are an approved AreaExpert of. Used for route
// validation (Permissions.RouteValidate) and area media management
// (Permissions.AreaEdit) — an ownership grant scoped to a query, not a claim,
// so it can't be expressed as a JWT claim like DynamicPermissions' grants.
public class AreaExpertAuthorizer(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    AreaHierarchyService areaHierarchy)
{
    // Builds a reusable per-area check, for callers stamping a can-X flag onto many
    // DTOs at once. Hits the DB only when the claim is absent.
    public async Task<Func<int, bool>> BuildAreaCheckAsync(ClaimsPrincipal user, string permission)
    {
        if (user.HasClaim("permission", permission))
        {
            return _ => true;
        }

        var userId = userManager.GetUserId(user)!;
        var expertAreaIds = await db.AreaExperts
            .Where(e => e.UserId == userId && e.Status == AreaExpertStatus.Approved)
            .Select(e => e.AreaId)
            .ToListAsync();

        if (expertAreaIds.Count == 0)
        {
            return _ => false;
        }

        var hierarchy = await areaHierarchy.GetAsync();
        return areaId => expertAreaIds.Contains(hierarchy.TopLevelId(areaId));
    }

    public async Task<bool> CanActOnAreaAsync(ClaimsPrincipal user, string permission, int areaId) =>
        (await BuildAreaCheckAsync(user, permission))(areaId);
}
