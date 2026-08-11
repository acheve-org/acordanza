using System.ComponentModel.DataAnnotations;
using System.Text;
using Acordanza.Api.Dtos;
using Acordanza.Core.Models;
using Acordanza.Api.Services;
using Acordanza.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/account")]
public class AccountController(
    UserManager<ApplicationUser> userManager,
    IConfiguration configuration,
    IEmailSender emailSender) : ControllerBase
{
    [HttpPost("change-password")]
    public async Task<ActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var error = result.Errors.FirstOrDefault()?.Description ?? "No se pudo cambiar la contraseña";
            return BadRequest(new { error });
        }

        return Ok(new { ok = true });
    }

    [HttpPut("profile")]
    public async Task<ActionResult> UpdateProfile(UpdateProfileRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var name = request.Name.Trim();
        if (name.Length is 0 or > 20)
        {
            return BadRequest(new { error = "El nombre debe tener entre 1 y 20 caracteres" });
        }

        user.Name = name;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var error = result.Errors.FirstOrDefault()?.Description ?? "No se pudo guardar el nombre";
            return BadRequest(new { error });
        }

        return Ok(new { name });
    }

    // Re-authenticates with the current password (same posture as ChangePassword)
    // before minting a change-email token, so a stolen-but-live JWT alone can't
    // walk the account off to an attacker-controlled address. The token itself
    // (GenerateChangeEmailTokenAsync) embeds the new address in its purpose
    // string and the current security stamp, so it can only ever confirm this
    // exact new address and dies the moment it's used or the stamp changes.
    [EnableRateLimiting("change-email")]
    [HttpPost("email/change-request")]
    public async Task<ActionResult> RequestEmailChange(ChangeEmailRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        if (!await userManager.CheckPasswordAsync(user, request.CurrentPassword))
        {
            return BadRequest(new { error = "Contraseña incorrecta" });
        }

        var newEmail = request.NewEmail.Trim();
        if (newEmail.Length == 0 || !new EmailAddressAttribute().IsValid(newEmail))
        {
            return BadRequest(new { error = "Correo electrónico no válido" });
        }

        if (string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Ese ya es tu correo electrónico actual" });
        }

        var existing = await userManager.FindByEmailAsync(newEmail);
        if (existing is not null)
        {
            return BadRequest(new { error = "Ese correo electrónico ya está en uso" });
        }

        var token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var frontendBaseUrl = configuration["Frontend:BaseUrl"]
            ?? throw new InvalidOperationException("Missing Frontend:BaseUrl configuration");
        var confirmLink = $"{frontendBaseUrl}/confirm-email-change" +
            $"?uid={Uri.EscapeDataString(user.Id)}&email={Uri.EscapeDataString(newEmail)}&token={encodedToken}";

        var (newSubject, newHtml) = EmailTemplates.ConfirmEmailChange(confirmLink, frontendBaseUrl);
        await emailSender.SendAsync(newEmail, newSubject, newHtml);

        // The heads-up goes to the *old* address at request time, not on completion:
        // once the change lands, password-reset mail goes to the new address, so a
        // notice sent afterwards would reach someone who can no longer recover the
        // account on their own. At request time the old owner still controls every
        // recovery path.
        var (oldSubject, oldHtml) = EmailTemplates.EmailChangeRequested(newEmail, frontendBaseUrl);
        await emailSender.SendAsync(user.Email!, oldSubject, oldHtml);

        return Ok(new { ok = true });
    }
}
