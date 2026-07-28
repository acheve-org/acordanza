using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/route-openers")]
public class RouteOpenersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<RouteOpenerDto>>> GetOpeners()
    {
        var openers = await db.RouteOpeners.AsNoTracking()
            .Include(o => o.User)
            .OrderBy(o => o.Name)
            .Select(o => new RouteOpenerDto(o.Id, o.Name, o.UserId, o.User != null ? o.User.UserName : null))
            .ToListAsync();

        return Ok(openers);
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<UserSummaryDto>>> GetLinkableUsers()
    {
        var users = await db.Users.AsNoTracking()
            .OrderBy(u => u.UserName)
            .Select(u => new UserSummaryDto(u.Id, u.UserName!))
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost]
    public async Task<ActionResult<RouteOpenerDto>> CreateOpener(CreateRouteOpenerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "el nombre es obligatorio" });
        }

        ApplicationUser? user = null;
        if (request.UserId is not null)
        {
            user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId);
            if (user is null) return BadRequest(new { error = "el usuario no existe" });
        }

        var opener = new RouteOpener { Name = request.Name.Trim(), UserId = user?.Id };
        db.RouteOpeners.Add(opener);
        await db.SaveChangesAsync();

        return Ok(new RouteOpenerDto(opener.Id, opener.Name, opener.UserId, user?.UserName));
    }
}
