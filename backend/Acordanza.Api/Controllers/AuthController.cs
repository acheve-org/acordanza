using System.Text;
using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Acordanza.Api.Dtos;
using Acordanza.Core.Models;
using Acordanza.Api.Services;
using Acordanza.Core.Services;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Acordanza.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    JwtTokenService tokenService,
    IFido2 fido2,
    IMemoryCache cache,
    IConfiguration configuration,
    IEmailSender emailSender,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new { error = "Correo electrónico o contraseña incorrectos" });
        }

        var passkeys = await db.PasskeyCredentials.Where(p => p.UserId == user.Id).ToListAsync();
        if (passkeys.Count == 0)
        {
            var token = await tokenService.CreateToken(user);
            return Ok(new LoginResponse(token, user.UserName!, false, null, null));
        }

        var options = fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = passkeys.Select(p => new PublicKeyCredentialDescriptor(p.CredentialId)).ToList(),
            UserVerification = UserVerificationRequirement.Preferred
        });

        var sessionId = Guid.NewGuid().ToString();
        cache.Set(CacheKey(sessionId), new LoginPasskeyChallenge(user.Id, options), TimeSpan.FromMinutes(5));

        return Ok(new LoginResponse(null, null, true, sessionId, options));
    }

    [EnableRateLimiting("PasskeyOptions")]
    [HttpPost("login/passkey/options")]
    public ActionResult<LoginResponse> LoginWithPasskeyOptions()
    {
        var options = fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = [],
            UserVerification = UserVerificationRequirement.Required
        });

        var sessionId = Guid.NewGuid().ToString();
        cache.Set(CacheKey(sessionId), new LoginPasskeyChallenge(null, options), TimeSpan.FromMinutes(5));

        return Ok(new LoginResponse(null, null, true, sessionId, options));
    }

    [HttpPost("login/passkey")]
    public async Task<ActionResult<LoginResponse>> LoginWithPasskey(PasskeyLoginRequest request)
    {
        if (!cache.TryGetValue(CacheKey(request.SessionId), out LoginPasskeyChallenge? challenge) || challenge is null)
        {
            return Unauthorized(new { error = "La solicitud ha caducado, vuelve a iniciar sesión" });
        }
        cache.Remove(CacheKey(request.SessionId));

        ApplicationUser? user;
        PasskeyCredential? stored;
        if (challenge.UserId is not null)
        {
            user = await userManager.FindByIdAsync(challenge.UserId);
            if (user is null) return Unauthorized();

            stored = await db.PasskeyCredentials
                .FirstOrDefaultAsync(p => p.UserId == user.Id && p.CredentialId == request.AssertionResponse.RawId);
        }
        else
        {
            stored = await db.PasskeyCredentials
                .FirstOrDefaultAsync(p => p.CredentialId == request.AssertionResponse.RawId);
            user = stored is null ? null : await userManager.FindByIdAsync(stored.UserId);
        }

        if (user is null || stored is null) return Unauthorized(new { error = "Llave de acceso desconocida" });

        try
        {
            var result = await fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = request.AssertionResponse,
                OriginalOptions = challenge.Options,
                StoredPublicKey = stored.PublicKey,
                StoredSignatureCounter = stored.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (p, _) =>
                    Task.FromResult(p.UserHandle.SequenceEqual(Encoding.UTF8.GetBytes(user.Id)))
            });

            stored.SignCount = result.SignCount;
            await db.SaveChangesAsync();
        }
        catch (Fido2VerificationException)
        {
            return Unauthorized(new { error = "No se pudo verificar la llave de acceso" });
        }

        var token = await tokenService.CreateToken(user);
        return Ok(new LoginResponse(token, user.UserName!, false, null, null));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<MeResponse>> Me()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var roles = await userManager.GetRolesAsync(user);
        var (permissions, isFirstAscensionist, isAreaExpert) = await DynamicPermissions.ResolveAsync(db, roles, user.Id);
        var role = roles.FirstOrDefault() ?? Roles.User;

        return Ok(new MeResponse(user.Id, user.UserName!, user.Name, role, permissions.ToArray(), isFirstAscensionist, isAreaExpert));
    }

    [EnableRateLimiting("forgot-password")]
    [HttpPost("forgot-password")]
    public async Task<ActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is not null)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var frontendBaseUrl = configuration["Frontend:BaseUrl"]
                ?? throw new InvalidOperationException("Missing Frontend:BaseUrl configuration");
            var resetLink = $"{frontendBaseUrl}/reset-password" +
                $"?email={Uri.EscapeDataString(user.Email!)}&token={encodedToken}";
            var (subject, html) = EmailTemplates.ResetPassword(resetLink, frontendBaseUrl);
            await emailSender.SendAsync(user.Email!, subject, html);
        }

        // Same response whether or not the account exists, so this endpoint
        // can't be used to enumerate registered emails.
        return Ok(new { ok = true });
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null) return BadRequest(new { error = "El enlace no es válido o ha caducado" });

        if (!TryDecodeToken(request.Token, out var token))
        {
            return BadRequest(new { error = "El enlace no es válido o ha caducado" });
        }

        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new { error = ResetPasswordErrorMessage(result) });
        }

        return Ok(new { ok = true });
    }

    // Lets both ResetPasswordScreen and AcceptInviteScreen check a link's token
    // before showing the form, instead of only finding out it's dead when the
    // user submits. Read-only: VerifyUserTokenAsync (unlike ResetPasswordAsync/
    // AddPasswordAsync+UpdateSecurityStampAsync) doesn't rotate the security
    // stamp, so checking here doesn't consume the token or affect the real
    // submit later. Both screens share this one endpoint/request shape, so a
    // token is accepted if it's valid for either purpose — a reset-password
    // token can never also verify against the invite provider (and vice versa),
    // since VerifyUserTokenAsync checks the provider it's asked to check.
    [HttpPost("validate-token")]
    public async Task<ActionResult> ValidateToken(ValidateTokenRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null) return BadRequest(new { error = "El enlace no es válido o ha caducado" });

        if (!TryDecodeToken(request.Token, out var token))
        {
            return BadRequest(new { error = "El enlace no es válido o ha caducado" });
        }

        var validForReset = await userManager.VerifyUserTokenAsync(
            user, userManager.Options.Tokens.PasswordResetTokenProvider, ResetPasswordTokenPurpose, token);
        var validForInvite = await userManager.VerifyUserTokenAsync(
            user, InviteTokens.ProviderName, InviteTokens.Purpose, token);
        if (!validForReset && !validForInvite)
        {
            return BadRequest(new { error = "El enlace no es válido o ha caducado" });
        }

        return Ok(new { ok = true });
    }

    // Completes an invite (see UsersController.InviteUser): sets the password via
    // the invite-specific single-use token (InviteTokens — a distinct provider/
    // purpose from forgot-password's, see Services/InviteTokenProvider.cs), and
    // records the display name chosen by the new user.
    [HttpPost("accept-invite")]
    public async Task<ActionResult> AcceptInvite(AcceptInviteRequest request)
    {
        var name = request.Name.Trim();
        if (name.Length is 0 or > 20)
        {
            return BadRequest(new { error = "El nombre debe tener entre 1 y 20 caracteres" });
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null) return BadRequest(new { error = "El enlace no es válido o ha caducado" });

        if (!TryDecodeToken(request.Token, out var token))
        {
            return BadRequest(new { error = "El enlace no es válido o ha caducado" });
        }

        var valid = await userManager.VerifyUserTokenAsync(user, InviteTokens.ProviderName, InviteTokens.Purpose, token);
        if (!valid)
        {
            return BadRequest(new { error = "El enlace no es válido o ha caducado" });
        }

        // Wrapped in a transaction so a failure partway through (e.g. the Name save)
        // rolls back the password/security-stamp changes too, rather than leaving the
        // account partially activated with its single-use token already burned and no
        // way to retry the same invite link.
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var addPasswordResult = await userManager.AddPasswordAsync(user, request.Password);
            if (!addPasswordResult.Succeeded)
            {
                return BadRequest(new { error = ResetPasswordErrorMessage(addPasswordResult) });
            }

            // AddPasswordAsync, unlike ResetPasswordAsync, doesn't rotate the security
            // stamp on its own — do it explicitly so this token can't be replayed.
            await userManager.UpdateSecurityStampAsync(user);

            user.Name = name;
            user.ConfirmedAt = DateTimeOffset.UtcNow;
            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                var error = updateResult.Errors.FirstOrDefault()?.Description ?? "No se pudo guardar el nombre";
                return BadRequest(new { error });
            }

            await transaction.CommitAsync();
        }

        // Best-effort from here on — the invite has already been fully accepted (password
        // set, name saved, single-use token consumed), so a notification delivery failure
        // must not fail the response and strand an otherwise-successful registration.
        var admins = await userManager.GetUsersInRoleAsync(Roles.Admin);
        foreach (var admin in admins.Where(a => a.Id != user.Id && !string.IsNullOrEmpty(a.Email)))
        {
            try
            {
                var frontendBaseUrl = configuration["Frontend:BaseUrl"]
                    ?? throw new InvalidOperationException("Missing Frontend:BaseUrl configuration");
                var (subject, html) = EmailTemplates.NewUserRegistered(user.Name, user.Email!, frontendBaseUrl);
                await emailSender.SendAsync(admin.Email!, subject, html);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to notify admin {AdminEmail} of new user {UserEmail}",
                    admin.Email, user.Email);
            }
        }

        return Ok(new { ok = true });
    }
    
    // Anonymous for the same reason reset-password/accept-invite are: the link
    // is opened in whatever browser reads the new inbox, which is usually not
    // a logged-in one. No validate-token pre-check here — this screen has no
    // form to protect from a dead link, it just submits on mount (see
    // ConfirmEmailChangeScreen) — and it wouldn't fit anyway: validate-token
    // looks accounts up by email and checks two fixed purposes, while this
    // token is found by user id and its purpose embeds the new address.
    [HttpPost("confirm-email-change")]
    public async Task<ActionResult> ConfirmEmailChange(ConfirmEmailChangeRequest request)
    {
        var user = await userManager.FindByIdAsync(request.UserId);
        if (user is null) return BadRequest(new { error = "El enlace no es válido o ha caducado" });

        if (!TryDecodeToken(request.Token, out var token))
        {
            return BadRequest(new { error = "El enlace no es válido o ha caducado" });
        }

        // Transaction: ChangeEmailAsync sets Email/EmailConfirmed and rotates the
        // stamp, but SetUserNameAsync is what keeps UserName == Email — the
        // invariant the rest of the app (MeResponse.Username, the JWT's
        // unique_name, login) relies on. Both must land together, or a failure
        // partway through leaves a half-changed identity with a spent, unretryable
        // token.
        await using var transaction = await db.Database.BeginTransactionAsync();

        var changeResult = await userManager.ChangeEmailAsync(user, request.NewEmail, token);
        if (!changeResult.Succeeded)
        {
            return BadRequest(new { error = "El enlace no es válido o ha caducado" });
        }

        var setUserNameResult = await userManager.SetUserNameAsync(user, request.NewEmail);
        if (!setUserNameResult.Succeeded)
        {
            var error = setUserNameResult.Errors.FirstOrDefault()?.Description ?? "El enlace no es válido o ha caducado";
            return BadRequest(new { error });
        }

        await transaction.CommitAsync();

        return Ok(new { ok = true });
    }

    // Matches UserManager's own internal purpose string for password-reset
    // tokens (it's not exposed publicly) — GeneratePasswordResetTokenAsync and
    // ResetPasswordAsync both use it under the hood, so VerifyUserTokenAsync
    // has to be called with the same value to check the same token here.
    private const string ResetPasswordTokenPurpose = "ResetPassword";

    private static bool TryDecodeToken(string encoded, out string token)
    {
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
            return true;
        }
        catch (FormatException)
        {
            token = "";
            return false;
        }
    }

    private static string ResetPasswordErrorMessage(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
        {
            return "El enlace no es válido o ha caducado";
        }

        return result.Errors.FirstOrDefault()?.Description ?? "El enlace no es válido o ha caducado";
    }

    private static string CacheKey(string sessionId) => $"login-passkey:{sessionId}";

    private record LoginPasskeyChallenge(string? UserId, AssertionOptions Options);
}
