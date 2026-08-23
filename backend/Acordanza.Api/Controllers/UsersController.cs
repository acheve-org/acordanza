using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text;
using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Acordanza.Core.Models;
using Acordanza.Api.Services;
using Acordanza.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/users")]
public class UsersController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    IConfiguration configuration) : ApiControllerBase
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;
    private const int DefaultMaxInvitesForUser = 5;
    private const int DefaultMaxInvitesForModerator = 10;
    private static readonly HashSet<string> SortableFields = ["name", "email", "role"];

    // Configurable via Invites:MaxForUser/Invites:MaxForModerator (see appsettings.json) rather
    // than hardcoded, so the lifetime invite cap can be tuned per deployment without a rebuild.
    // Admins are unlimited regardless (checked separately via the UserManage claim).
    private int GetInviteLimit() =>
        User.IsInRole(Roles.Moderator)
            ? configuration.GetValue("Invites:MaxForModerator", DefaultMaxInvitesForModerator)
            : configuration.GetValue("Invites:MaxForUser", DefaultMaxInvitesForUser);

    [Authorize(Policy = Permissions.UserManage)]
    [HttpGet]
    public async Task<ActionResult<PagedUsersResponse>> GetUsers(
        [FromQuery] string? q, [FromQuery] string? role,
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string sortBy = "name", [FromQuery] string sortDir = "asc")
    {
        (page, pageSize) = PageRequest.Clamp(page, pageSize, DefaultPageSize, MaxPageSize);
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
                InvitedByName = user.InvitedBy != null ? user.InvitedBy.Name : null,
                user.InvitedAt,
                user.ConfirmedAt,
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
        var dtos = items.Select(u => new UserAccountDto(
            u.Id, u.Name, u.Email, u.Role, u.Pending, u.InvitedByName, u.InvitedAt, u.ConfirmedAt)).ToList();

        return Ok(new PagedUsersResponse(dtos, totalCount, page, pageSize));
    }

    // Lets the current user manage their own invites (see InviteUser) from the
    // "Mi acordanza" section, regardless of whether they can see/manage every
    // account (that's GetUsers, admin-only).
    [Authorize(Policy = Permissions.UserInvite)]
    [HttpGet("my-invites")]
    public async Task<ActionResult<MyInvitesResponse>> GetMyInvites()
    {
        var callerId = CallerId;
        var unlimited = User.HasClaim("permission", Permissions.UserManage);
        var maxInvites = unlimited ? (int?)null : GetInviteLimit();

        var invites = await db.Users.AsNoTracking()
            .Where(u => u.InvitedByUserId == callerId)
            .OrderBy(u => u.Email)
            .Select(u => new MyInviteDto(
                u.Email!,
                u.PasswordHash == null,
                db.Users.Any(u2 => u2.InvitedByUserId == u.Id)))
            .ToListAsync();

        var invitesLeft = maxInvites is null ? null : (int?)Math.Max(0, maxInvites.Value - invites.Count);
        return Ok(new MyInvitesResponse(invites, invitesLeft, maxInvites));
    }

    // Creates the account up front (with no usable password — CheckPasswordAsync fails
    // until AcceptInvite sets one via the invite token mechanism, see InviteTokens) so
    // the invite link can carry a real, single-use token. Available to every user (see
    // Permissions.UserInvite), not just admins: an admin (User has the UserManage
    // claim) can invite anyone as any role with no limit; anyone else can only invite
    // as a plain user, capped at MaxInvitesForNonAdmins lifetime invites.
    [Authorize(Policy = Permissions.UserInvite)]
    [HttpPost("invite")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ApiError), 400)]
    [ProducesResponseType(typeof(ApiError), 409)]
    public async Task<ActionResult> InviteUser(InviteUserRequest request)
    {
        var email = request.Email.Trim();
        if (email.Length == 0 || !new EmailAddressAttribute().IsValid(email))
        {
            return BadRequest(new ApiError("Introduce un correo electrónico válido"));
        }

        var callerId = CallerId;
        var unlimited = User.HasClaim("permission", Permissions.UserManage);
        var role = unlimited ? request.Role : Roles.User;
        if (!Roles.All.Contains(role))
        {
            return BadRequest(new ApiError("Rol no válido"));
        }

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null && existing.PasswordHash is not null)
        {
            return Conflict(new ApiError("Ya existe una cuenta con ese correo"));
        }

        // A pending invite for this email already exists — most likely a previous
        // send failed after the account/role were created (config error, email
        // provider outage, ...), which would otherwise strand it behind a Conflict
        // with no way to recover. The original inviter (or an admin) can always
        // resend/correct it; anyone else is blocked while it's still within its
        // 7-day window (InviteTokens.Lifespan) — this is what stops a pending
        // invite from being silently duplicated by a second inviter. Past that
        // window it's fair game and reassigns to whoever invites it next.
        var isTakeover = false;
        if (existing is not null)
        {
            var isOwnInvite = existing.InvitedByUserId == callerId;
            var stillPending = existing.InvitedAt is { } invitedAt
                && invitedAt.Add(InviteTokens.Lifespan) > DateTimeOffset.UtcNow;
            if (!unlimited && !isOwnInvite && stillPending)
            {
                return Conflict(new ApiError("Ya hay una invitación pendiente para ese correo"));
            }
            isTakeover = !isOwnInvite;
        }

        // Serializable so the cap check below can't race with a concurrent invite from
        // the same non-admin caller: two requests both reading a count under the limit
        // and both inserting would otherwise let them exceed it together. Postgres
        // detects that write skew under Serializable and aborts one side with a
        // 40001 error, caught below and reported as a conflict to retry.
        ApplicationUser user;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            try
            {
                if (!unlimited && (existing is null || isTakeover))
                {
                    var usedCount = await db.Users.CountAsync(u => u.InvitedByUserId == callerId);
                    var maxInvites = GetInviteLimit();
                    if (usedCount >= maxInvites)
                    {
                        return Conflict(new ApiError($"Has alcanzado el límite de {maxInvites} invitaciones"));
                    }
                }

                if (existing is not null)
                {
                    user = existing;
                    if (isTakeover)
                    {
                        user.InvitedByUserId = callerId;
                    }
                    user.InvitedAt = DateTimeOffset.UtcNow;

                    // Not gated to admins only: a non-admin taking over someone else's expired
                    // invite (isTakeover, see above) must also demote it to Roles.User — `role`
                    // is already forced to Roles.User for non-admins, but without this the
                    // account would keep whatever elevated role its original (e.g. admin)
                    // inviter assigned, letting a non-admin activate a privileged account.
                    var currentRoles = await userManager.GetRolesAsync(user);
                    if (!currentRoles.Contains(role))
                    {
                        var removeResult = await userManager.RemoveFromRolesAsync(user, currentRoles);
                        if (!removeResult.Succeeded)
                        {
                            var error = removeResult.Errors.FirstOrDefault()?.Description ?? "No se pudo actualizar el rol";
                            return BadRequest(new ApiError(error));
                        }

                        var addRoleResult = await userManager.AddToRoleAsync(user, role);
                        if (!addRoleResult.Succeeded)
                        {
                            await userManager.AddToRolesAsync(user, currentRoles);
                            var error = addRoleResult.Errors.FirstOrDefault()?.Description ?? "No se pudo asignar el rol";
                            return BadRequest(new ApiError(error));
                        }
                    }

                    var updateResult = await userManager.UpdateAsync(user);
                    if (!updateResult.Succeeded)
                    {
                        var error = updateResult.Errors.FirstOrDefault()?.Description ?? "No se pudo actualizar la invitación";
                        return BadRequest(new ApiError(error));
                    }
                }
                else
                {
                    user = new ApplicationUser
                    {
                        UserName = email,
                        Email = email,
                        InvitedByUserId = callerId,
                        InvitedAt = DateTimeOffset.UtcNow
                    };
                    var createResult = await userManager.CreateAsync(user);
                    if (!createResult.Succeeded)
                    {
                        var error = createResult.Errors.FirstOrDefault()?.Description ?? "No se pudo crear la invitación";
                        return BadRequest(new ApiError(error));
                    }

                    var roleResult = await userManager.AddToRoleAsync(user, role);
                    if (!roleResult.Succeeded)
                    {
                        await userManager.DeleteAsync(user);
                        var error = roleResult.Errors.FirstOrDefault()?.Description ?? "No se pudo asignar el rol";
                        return BadRequest(new ApiError(error));
                    }
                }

                await transaction.CommitAsync();
            }
            catch (Exception ex) when (IsSerializationFailure(ex))
            {
                return Conflict(new ApiError("Ha habido un conflicto al procesar la invitación, inténtalo de nuevo"));
            }
        }

        var token = await userManager.GenerateUserTokenAsync(user, InviteTokens.ProviderName, InviteTokens.Purpose);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var frontendBaseUrl = configuration["Frontend:BaseUrl"]
            ?? throw new InvalidOperationException("Missing Frontend:BaseUrl configuration");
        var inviteLink = $"{frontendBaseUrl}/accept-invite" +
            $"?email={Uri.EscapeDataString(email)}&token={encodedToken}";
        var (subject, html) = EmailTemplates.InviteUser(inviteLink, frontendBaseUrl);
        await emailSender.SendAsync(email, subject, html);

        return Ok(new { ok = true });
    }

    // EF Core's default execution strategy wraps transient failures in its own
    // InvalidOperationException (itself wrapping the DbUpdateException that wraps the
    // real PostgresException), so the serialization failure can be nested two levels
    // deep rather than one — walk the whole chain instead of checking InnerException.
    private static bool IsSerializationFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
            {
                return true;
            }
        }
        return false;
    }
}
