using System.ComponentModel.DataAnnotations;
using System.Text;
using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Acordanza.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Controllers;

[Authorize(Policy = Permissions.UserManage)]
[ApiController]
[Route("api/users")]
public class UsersController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    IConfiguration configuration) : ControllerBase
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;
    private static readonly HashSet<string> SortableFields = ["name", "email", "role"];

    [HttpGet]
    public async Task<ActionResult<PagedUsersResponse>> GetUsers(
        [FromQuery] string? q, [FromQuery] string? role,
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string sortBy = "name", [FromQuery] string sortDir = "asc")
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > MaxPageSize) pageSize = DefaultPageSize;
        if (!SortableFields.Contains(sortBy)) sortBy = "name";

        // A plain join on UserRoles would emit one row per user-role pair, which
        // would double-count (and mis-paginate) any account ever assigned more
        // than one role. RoleSeeder/create-user only ever assign one, but nothing
        // enforces that invariant at the data level, so this picks a single,
        // deterministic role per user via a correlated subquery instead.
        var query =
            from user in db.Users.AsNoTracking()
            select new
            {
                user.Id,
                user.Name,
                Email = user.Email!,
                // No password hash means the account was invited but the invitee hasn't
                // completed registration yet (see InviteUser/AuthController.AcceptInvite).
                Pending = user.PasswordHash == null,
                Role = (from userRole in db.UserRoles
                        join r in db.Roles on userRole.RoleId equals r.Id
                        where userRole.UserId == user.Id
                        orderby r.Name
                        select r.Name).FirstOrDefault() ?? Roles.User
            };

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(u => u.Name.Contains(q) || u.Email.Contains(q) || u.Role.Contains(q));
        }
        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(u => u.Role == role);
        }

        var totalCount = await query.CountAsync();

        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy, descending) switch
        {
            ("email", true) => query.OrderByDescending(u => u.Email),
            ("email", false) => query.OrderBy(u => u.Email),
            ("role", true) => query.OrderByDescending(u => u.Role),
            ("role", false) => query.OrderBy(u => u.Role),
            (_, true) => query.OrderByDescending(u => u.Name),
            _ => query.OrderBy(u => u.Name)
        };

        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var dtos = items.Select(u => new UserAccountDto(u.Id, u.Name, u.Email, u.Role, u.Pending)).ToList();

        return Ok(new PagedUsersResponse(dtos, totalCount, page, pageSize));
    }

    // Creates the account up front (with no usable password — CheckPasswordAsync fails
    // until AcceptInvite sets one via the same reset-password token mechanism the
    // forgot-password flow uses) so the invite link can carry a real, single-use token.
    [HttpPost("invite")]
    public async Task<ActionResult> InviteUser(InviteUserRequest request)
    {
        var email = request.Email.Trim();
        if (email.Length == 0 || !new EmailAddressAttribute().IsValid(email))
        {
            return BadRequest(new { error = "Introduce un correo electrónico válido" });
        }
        if (!Roles.All.Contains(request.Role))
        {
            return BadRequest(new { error = "Rol no válido" });
        }

        var existing = await userManager.FindByEmailAsync(email);
        ApplicationUser user;
        if (existing is not null)
        {
            if (existing.PasswordHash is not null)
            {
                return Conflict(new { error = "Ya existe una cuenta con ese correo" });
            }

            // A pending invite for this email already exists — most likely a previous
            // send failed after the account/role were created (config error, email
            // provider outage, ...), which would otherwise strand it behind this same
            // Conflict with no way to recover. Treat this as a resend instead, updating
            // the role in case the admin is also correcting it.
            user = existing;
            var currentRoles = await userManager.GetRolesAsync(user);
            if (!currentRoles.Contains(request.Role))
            {
                var removeResult = await userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!removeResult.Succeeded)
                {
                    var error = removeResult.Errors.FirstOrDefault()?.Description ?? "No se pudo actualizar el rol";
                    return BadRequest(new { error });
                }

                var addRoleResult = await userManager.AddToRoleAsync(user, request.Role);
                if (!addRoleResult.Succeeded)
                {
                    await userManager.AddToRolesAsync(user, currentRoles);
                    var error = addRoleResult.Errors.FirstOrDefault()?.Description ?? "No se pudo asignar el rol";
                    return BadRequest(new { error });
                }
            }
        }
        else
        {
            user = new ApplicationUser { UserName = email, Email = email };
            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                var error = createResult.Errors.FirstOrDefault()?.Description ?? "No se pudo crear la invitación";
                return BadRequest(new { error });
            }

            var roleResult = await userManager.AddToRoleAsync(user, request.Role);
            if (!roleResult.Succeeded)
            {
                await userManager.DeleteAsync(user);
                var error = roleResult.Errors.FirstOrDefault()?.Description ?? "No se pudo asignar el rol";
                return BadRequest(new { error });
            }
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var frontendBaseUrl = configuration["Frontend:BaseUrl"]
            ?? throw new InvalidOperationException("Missing Frontend:BaseUrl configuration");
        var inviteLink = $"{frontendBaseUrl}/accept-invite" +
            $"?email={Uri.EscapeDataString(email)}&token={encodedToken}";
        var (subject, html) = EmailTemplates.InviteUser(inviteLink);
        await emailSender.SendAsync(email, subject, html);

        return Ok(new { ok = true });
    }
}
