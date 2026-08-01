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
    public async Task<ActionResult<List<FirstAscensionistDto>>> GetFirstAscensionists([FromQuery] string? q)
    {
        var query = db.FirstAscensionists.AsNoTracking().Include(o => o.User).AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(o => o.Name.Contains(q));
        }

        var firstAscensionists = await query
            .OrderBy(o => o.Name)
            .Select(o => new FirstAscensionistDto(o.Id, o.Name, o.UserId, o.User != null ? o.User.Name : null))
            .ToListAsync();

        return Ok(firstAscensionists);
    }

    // Creates an unlinked FirstAscensionist record (e.g. a historic route opener
    // with no account yet). Linking one to a user account always goes through
    // FirstAscensionistLinksController's request/approve flow now, even for
    // admins/moderators, so who-linked-what-when is always tracked.
    [Authorize(Policy = Permissions.FirstAscensionistCreate)]
    [HttpPost]
    public async Task<ActionResult<FirstAscensionistDto>> CreateFirstAscensionist(CreateFirstAscensionistRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "el nombre es obligatorio" });
        }

        var firstAscensionist = new FirstAscensionist { Name = request.Name.Trim() };
        db.FirstAscensionists.Add(firstAscensionist);
        await db.SaveChangesAsync();

        return Ok(new FirstAscensionistDto(firstAscensionist.Id, firstAscensionist.Name, null, null));
    }
}
