using System.Text;
using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Acordanza.Api.Services;
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
            var (subject, html) = EmailTemplates.ResetPassword(resetLink);
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
    // user submits. Read-only: VerifyUserTokenAsync (unlike ResetPasswordAsync)
    // doesn't rotate the security stamp, so checking here doesn't consume the
    // token or affect the real submit later.
    [HttpPost("validate-token")]
    public async Task<ActionResult> ValidateToken(ValidateTokenRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null) return BadRequest(new { error = "El enlace no es válido o ha caducado" });

        if (!TryDecodeToken(request.Token, out var token))
        {
            return BadRequest(new { error = "El enlace no es válido o ha caducado" });
        }

        var valid = await userManager.VerifyUserTokenAsync(
            user, userManager.Options.Tokens.PasswordResetTokenProvider, ResetPasswordTokenPurpose, token);
        if (!valid)
        {
            return BadRequest(new { error = "El enlace no es válido o ha caducado" });
        }

        return Ok(new { ok = true });
    }

    // Completes an admin-issued invite (see UsersController.InviteUser): sets the
    // password via the same single-use reset-token mechanism forgot-password uses,
    // and records the display name chosen by the new user.
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

        var result = await userManager.ResetPasswordAsync(user, token, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new { error = ResetPasswordErrorMessage(result) });
        }

        user.Name = name;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            var error = updateResult.Errors.FirstOrDefault()?.Description ?? "No se pudo guardar el nombre";
            return BadRequest(new { error });
        }

        // Best-effort from here on — the invite has already been fully accepted (password
        // set, name saved, single-use token consumed), so a notification delivery failure
        // must not fail the response and strand an otherwise-successful registration.
        var admins = await userManager.GetUsersInRoleAsync(Roles.Admin);
        foreach (var admin in admins.Where(a => a.Id != user.Id && !string.IsNullOrEmpty(a.Email)))
        {
            try
            {
                var (subject, html) = EmailTemplates.NewUserRegistered(user.Name, user.Email!);
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
