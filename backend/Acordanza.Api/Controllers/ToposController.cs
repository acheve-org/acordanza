using Acordanza.Core.Data;
using Acordanza.Api.Services;
using Acordanza.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/topos")]
public class ToposController(AppDbContext db, BlobStorageService blobStorage) : ControllerBase
{
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteTopo(int id)
    {
        var topo = await db.Topos.FindAsync(id);
        if (topo is not null)
        {
            await blobStorage.DeleteAsync(topo.BlobName);
            db.Topos.Remove(topo);
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }
}
