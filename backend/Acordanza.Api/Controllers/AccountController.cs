using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/account")]
public class AccountController(UserManager<ApplicationUser> userManager) : ControllerBase
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
}
