using Acordanza.Api.Data;
using Acordanza.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Authorization;

// Ensures the fixed role catalog exists and that every account has at least
// one role, so pre-existing accounts (created before roles/permissions were
// introduced) keep working under the new policy-gated endpoints.
public static class RoleSeeder
{
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

        var db = services.GetRequiredService<AppDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var usersWithRoles = await db.UserRoles.Select(ur => ur.UserId).Distinct().ToListAsync();
        var unroledUsers = await db.Users
            .Where(u => !usersWithRoles.Contains(u.Id))
            .ToListAsync();

        foreach (var user in unroledUsers)
        {
            await userManager.AddToRoleAsync(user, Roles.User);
        }
    }
}
