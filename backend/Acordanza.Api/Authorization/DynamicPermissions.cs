using Acordanza.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Authorization;

// Resolves a user's full permission set: the static role-derived ones, plus
// (since the first-ascensionist role was retired) route.edit/route.validate/
// first-ascensionist.edit for anyone linked to a FirstAscensionist record —
// shared by JwtTokenService (JWT claims) and AuthController.Me (the response
// the frontend reads to decide what to show), so the two can't drift apart.
public static class DynamicPermissions
{
    private static readonly string[] FirstAscensionistLinkGrant =
    [
        Permissions.RouteEdit,
        Permissions.RouteValidate,
        Permissions.FirstAscensionistEdit
    ];

    public static async Task<(IReadOnlyCollection<string> Permissions, bool IsFirstAscensionist)> ResolveAsync(
        AppDbContext db, IEnumerable<string> roles, string userId)
    {
        var isFirstAscensionist = await db.FirstAscensionists.AnyAsync(f => f.UserId == userId);
        IReadOnlyCollection<string> permissions = RolePermissions.PermissionsFor(roles);
        if (isFirstAscensionist)
        {
            permissions = permissions.Union(FirstAscensionistLinkGrant).ToArray();
        }

        return (permissions, isFirstAscensionist);
    }
}
