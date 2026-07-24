using System.Text;
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
    IEmailSender emailSender) : ControllerBase
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
            var token = tokenService.CreateToken(user);
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

        var token = tokenService.CreateToken(user);
        return Ok(new LoginResponse(token, user.UserName!, false, null, null));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<MeResponse>> Me()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();
        return Ok(new MeResponse(user.UserName!));
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
            await emailSender.SendAsync(user.Email!, "Restablece tu contraseña de Acordanza",
                BuildResetPasswordEmailHtml(resetLink));
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

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));
        }
        catch (FormatException)
        {
            return BadRequest(new { error = "El enlace no es válido o ha caducado" });
        }

        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            var error = result.Errors.FirstOrDefault()?.Description ?? "El enlace no es válido o ha caducado";
            return BadRequest(new { error });
        }

        return Ok(new { ok = true });
    }

    private static string BuildResetPasswordEmailHtml(string resetLink) => $"""
        <p>Has solicitado restablecer tu contraseña de Acordanza.</p>
        <p><a href="{resetLink}">Haz clic aquí para elegir una nueva contraseña</a>.</p>
        <p>Si tú no has solicitado esto, puedes ignorar este correo.</p>
        """;

    private static string CacheKey(string sessionId) => $"login-passkey:{sessionId}";

    private record LoginPasskeyChallenge(string? UserId, AssertionOptions Options);
}
