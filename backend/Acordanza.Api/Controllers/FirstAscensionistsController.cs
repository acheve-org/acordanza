using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/first-ascensionists")]
public class FirstAscensionistsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<FirstAscensionistDto>>> GetFirstAscensionists()
    {
        var firstAscensionists = await db.FirstAscensionists.AsNoTracking()
            .Include(o => o.User)
            .OrderBy(o => o.Name)
            .Select(o => new FirstAscensionistDto(o.Id, o.Name, o.UserId, o.User != null ? o.User.Name : null))
            .ToListAsync();

        return Ok(firstAscensionists);
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<UserSummaryDto>>> GetLinkableUsers()
    {
        var users = await db.Users.AsNoTracking()
            .OrderBy(u => u.Name)
            .Select(u => new UserSummaryDto(u.Id, u.Name))
            .ToListAsync();

        return Ok(users);
    }

    [Authorize(Policy = Permissions.FirstAscensionistCreate)]
    [HttpPost]
    public async Task<ActionResult<FirstAscensionistDto>> CreateFirstAscensionist(CreateFirstAscensionistRequest request)
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

        var firstAscensionist = new FirstAscensionist { Name = request.Name.Trim(), UserId = user?.Id };
        db.FirstAscensionists.Add(firstAscensionist);
        await db.SaveChangesAsync();

        return Ok(new FirstAscensionistDto(firstAscensionist.Id, firstAscensionist.Name, firstAscensionist.UserId, user?.Name));
    }
}
