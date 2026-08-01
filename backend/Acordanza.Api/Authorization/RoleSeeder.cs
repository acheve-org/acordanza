using Acordanza.Api.Data;
using Acordanza.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Acordanza.Api.Authorization;

// Ensures the fixed role catalog exists and that every account has at least
// one role, so pre-existing accounts (created before roles/permissions were
// introduced) keep working under the new policy-gated endpoints.
public static class RoleSeeder
{
    // Removed from the role catalog when first-ascensionist edit rights became a
    // dynamic per-user grant instead of a role (see DynamicPermissions) — kept
    // here, not in Roles.All, purely so this cleanup can still find and retire
    // any account still holding it from before the change.
    private const string LegacyFirstAscensionistRole = "first-ascensionist";

    public static async Task EnsureSeededAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<AppDbContext>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(RoleSeeder));
        await RetireLegacyFirstAscensionistRoleAsync(roleManager, userManager, db, logger);

        var usersWithRoles = await db.UserRoles.Select(ur => ur.UserId).Distinct().ToListAsync();
        var unroledUsers = await db.Users
            .Where(u => !usersWithRoles.Contains(u.Id))
            .ToListAsync();

        foreach (var user in unroledUsers)
        {
            await userManager.AddToRoleAsync(user, Roles.User);
        }
    }

    // One-time (but idempotent, so safe to run on every startup) migration off the
    // retired first-ascensionist role: anyone still holding it drops to the plain
    // user role, then the role row itself is deleted. A no-op once cleaned up, or
    // on a fresh database that never had the role to begin with.
    //
    // This is a deliberate, confirmed trade-off, not an oversight: an account that
    // held the role only because of a direct admin grant (not because it's linked
    // to a FirstAscensionist record) loses route.edit/route.validate/
    // first-ascensionist.edit until it goes through the new claim flow. That
    // account is logged below so the drop isn't silent.
    private static async Task RetireLegacyFirstAscensionistRoleAsync(
        RoleManager<IdentityRole> roleManager, UserManager<ApplicationUser> userManager,
        AppDbContext db, ILogger logger)
    {
        if (!await roleManager.RoleExistsAsync(LegacyFirstAscensionistRole)) return;

        var legacyUsers = await userManager.GetUsersInRoleAsync(LegacyFirstAscensionistRole);
        foreach (var user in legacyUsers)
        {
            await userManager.RemoveFromRoleAsync(user, LegacyFirstAscensionistRole);
            if (!await userManager.IsInRoleAsync(user, Roles.User))
            {
                await userManager.AddToRoleAsync(user, Roles.User);
            }

            var isLinkedFirstAscensionist = await db.FirstAscensionists.AnyAsync(f => f.UserId == user.Id);
            if (!isLinkedFirstAscensionist)
            {
                logger.LogWarning(
                    "Account {Email} held the legacy first-ascensionist role but has no linked " +
                    "FirstAscensionist record — it lost route.edit/route.validate/first-ascensionist.edit " +
                    "and will need to go through the new claim flow to regain them.",
                    user.Email);
            }
        }

        var role = await roleManager.FindByNameAsync(LegacyFirstAscensionistRole);
        if (role is not null)
        {
            await roleManager.DeleteAsync(role);
        }
    }
}
