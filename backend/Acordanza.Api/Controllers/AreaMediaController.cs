using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Models;
using Acordanza.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/area-media")]
public class AreaMediaController(AppDbContext db, BlobStorageService blobStorage, UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    private sealed record AreaNode(int Id, int? ParentAreaId);

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteAreaMedia(int id)
    {
        var media = await db.AreaMedia.FindAsync(id);
        if (media is null)
        {
            return Ok(new { ok = true });
        }

        if (!await CanManageAreaMediaAsync(media.AreaId))
        {
            return Forbid();
        }

        await blobStorage.DeleteAsync(media.BlobName);
        db.AreaMedia.Remove(media);
        await db.SaveChangesAsync();

        return Ok(new { ok = true });
    }

    // Same "admins/moderators, or an approved AreaExpert of the top-level ancestor"
    // check as AreasController.BuildCanManageAreaMediaCheckAsync — duplicated rather
    // than shared, matching this codebase's existing per-controller helper pattern.
    private async Task<bool> CanManageAreaMediaAsync(int areaId)
    {
        if (User.HasClaim("permission", Permissions.AreaEdit))
        {
            return true;
        }

        var userId = userManager.GetUserId(User)!;
        var expertAreaIds = await db.AreaExperts
            .Where(e => e.UserId == userId && e.Status == AreaExpertStatus.Approved)
            .Select(e => e.AreaId)
            .ToListAsync();

        if (expertAreaIds.Count == 0)
        {
            return false;
        }

        var areaById = await db.Areas.AsNoTracking()
            .Select(a => new AreaNode(a.Id, a.ParentAreaId))
            .ToDictionaryAsync(a => a.Id);

        var topLevelId = areaId;
        int? currentId = areaId;
        while (currentId is int aid && areaById.TryGetValue(aid, out var area))
        {
            topLevelId = aid;
            currentId = area.ParentAreaId;
        }

        return expertAreaIds.Contains(topLevelId);
    }
}
