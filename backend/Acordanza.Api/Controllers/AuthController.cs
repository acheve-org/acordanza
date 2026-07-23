using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Acordanza.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Acordanza.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(UserManager<ApplicationUser> userManager, JwtTokenService tokenService)
    : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByNameAsync(request.Username);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new { error = "Usuario o contraseña incorrectos" });
        }

        var token = tokenService.CreateToken(user);
        return Ok(new LoginResponse(token, user.UserName!));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<MeResponse>> Me()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();
        return Ok(new MeResponse(user.UserName!));
    }
}
