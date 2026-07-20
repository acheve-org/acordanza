using Acordanza.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/ticks")]
public class TicksController(AppDbContext db) : ControllerBase
{
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteTick(int id)
    {
        var tick = await db.Ticks.FindAsync(id);
        if (tick is not null)
        {
            db.Ticks.Remove(tick);
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }
}
