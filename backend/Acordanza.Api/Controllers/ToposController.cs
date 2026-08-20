using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Acordanza.Api.Services;
using Acordanza.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/topos")]
public class ToposController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    BlobStorageService blobStorage,
    AreaExpertAuthorizer areaExpertAuthorizer) : ControllerBase
{
    // Uploading a topo stays open to every account (RoutesController.UploadTopo): adding a
    // photo to a route is additive, and a shared log people actually add photos to is the
    // point. Deleting one is not symmetric with that — the blob goes immediately and nothing
    // brings it back — so it takes the same shape TicksController.DeleteTick uses for a
    // broadly-granted permission (scope to the caller's own row), widened by the same
    // area-scoped check AreaMediaController.DeleteAreaMedia applies to the identical feature
    // on the area side: whoever uploaded it, plus anyone who can edit the route it hangs off
    // (moderators/admins, linked first ascensionists, and approved experts of that area).
    //
    // Until this check existed, any authenticated account could delete any topo by id.
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteTopo(int id)
    {
        var topo = await db.Topos.Include(t => t.Route).FirstOrDefaultAsync(t => t.Id == id);
        if (topo is null)
        {
            // Already gone — the same silent success AreaMediaController returns, so a
            // double-click on the delete button doesn't surface an error.
            return Ok(new { ok = true });
        }

        if (!await CanDeleteAsync(topo))
        {
            return Forbid();
        }

        await blobStorage.DeleteAsync(topo.BlobName);
        db.Topos.Remove(topo);
        await db.SaveChangesAsync();

        return Ok(new { ok = true });
    }

    private async Task<bool> CanDeleteAsync(Topo topo) =>
        topo.UploadedByUserId == userManager.GetUserId(User) ||
        await areaExpertAuthorizer.CanActOnAreaAsync(User, Permissions.RouteEdit, topo.Route.AreaId);
}
