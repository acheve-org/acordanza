using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Acordanza.Api.Services;
using Acordanza.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/area-media")]
public class AreaMediaController(AppDbContext db, BlobStorageService blobStorage, AreaExpertAuthorizer areaExpertAuthorizer)
    : ControllerBase
{
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteAreaMedia(int id)
    {
        var media = await db.AreaMedia.FindAsync(id);
        if (media is null)
        {
            return Ok(new { ok = true });
        }

        // Admins/moderators (area.edit) can manage any area's media; otherwise only
        // an approved AreaExpert of the area's top-level ancestor can — see
        // AreaExpertAuthorizer, the same rule AreasController.UploadAreaMedia uses.
        if (!await areaExpertAuthorizer.CanActOnAreaAsync(User, Permissions.AreaEdit, media.AreaId))
        {
            return Forbid();
        }

        await blobStorage.DeleteAsync(media.BlobName);
        db.AreaMedia.Remove(media);
        await db.SaveChangesAsync();

        return Ok(new { ok = true });
    }
}
