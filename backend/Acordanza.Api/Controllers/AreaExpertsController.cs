using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Acordanza.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/area-experts")]
public class AreaExpertsController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    ILogger<AreaExpertsController> logger) : ControllerBase
{
    [HttpGet("mine")]
    public async Task<ActionResult<List<AreaExpertDto>>> GetMine()
    {
        var userId = userManager.GetUserId(User)!;
        var experts = await Query().Where(e => e.UserId == userId).OrderBy(e => e.Area.Name).ToListAsync();
        return Ok(experts.Select(ToDto).ToList());
    }

    [Authorize(Policy = Permissions.AreaExpertApprove)]
    [HttpGet("pending")]
    public async Task<ActionResult<List<AreaExpertDto>>> GetPending()
    {
        var experts = await Query().Where(e => e.ApprovedAt == null).OrderBy(e => e.RequestedAt).ToListAsync();
        return Ok(experts.Select(ToDto).ToList());
    }

    [Authorize(Policy = Permissions.AreaExpertRequest)]
    [HttpPost]
    public async Task<ActionResult<AreaExpertDto>> RequestExpert(CreateAreaExpertRequest request)
    {
        var area = await db.Areas.FindAsync(request.AreaId);
        if (area is null) return BadRequest(new { error = "el sector no existe" });
        if (area.ParentAreaId is not null) return BadRequest(new { error = "solo los sectores principales admiten expertos" });

        var userId = userManager.GetUserId(User)!;
        if (await db.AreaExperts.AnyAsync(e => e.AreaId == request.AreaId && e.UserId == userId))
        {
            return BadRequest(new { error = "ya has solicitado ser experto de este sector" });
        }

        var expert = new AreaExpert { AreaId = request.AreaId, UserId = userId };
        db.AreaExperts.Add(expert);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Lost a race against another request for the same (AreaId, UserId) pair —
            // the AnyAsync check above passed for both, but the unique index only lets
            // one insert through.
            return BadRequest(new { error = "ya has solicitado ser experto de este sector" });
        }

        var saved = await Query().FirstAsync(e => e.Id == expert.Id);
        return Ok(ToDto(saved));
    }

    [Authorize(Policy = Permissions.AreaExpertApprove)]
    [HttpPost("{id:int}/approve")]
    public async Task<ActionResult<AreaExpertDto>> Approve(int id)
    {
        var approverId = userManager.GetUserId(User)!;
        var approvedAt = DateTimeOffset.UtcNow;

        // A single conditional UPDATE, not a load-check-save round trip: if two
        // reviewers approve concurrently, only the request that still finds
        // ApprovedAt null when the UPDATE runs affects a row — the loser gets 0
        // rows affected instead of silently overwriting the first approval.
        var updated = await db.AreaExperts
            .Where(e => e.Id == id && e.ApprovedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.ApprovedAt, approvedAt)
                .SetProperty(e => e.ApprovedByUserId, approverId));

        if (updated == 0)
        {
            var exists = await db.AreaExperts.AnyAsync(e => e.Id == id);
            return exists ? BadRequest(new { error = "ya está aprobada" }) : NotFound(new { error = "No encontrado" });
        }

        var saved = await Query().FirstAsync(e => e.Id == id);

        // Best-effort: the approval itself already succeeded, so a delivery
        // failure here must not fail the response.
        if (!string.IsNullOrEmpty(saved.User.Email))
        {
            try
            {
                var (subject, html) = EmailTemplates.AreaExpertApproved(saved.Area.Name);
                await emailSender.SendAsync(saved.User.Email, subject, html);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to notify {UserEmail} of area-expert approval for area {AreaId}",
                    saved.User.Email, saved.AreaId);
            }
        }

        return Ok(ToDto(saved));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        var expert = await db.AreaExperts.Include(e => e.Area).Include(e => e.User)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (expert is null) return NotFound(new { error = "No encontrado" });

        var userId = userManager.GetUserId(User)!;
        var canManage = User.HasClaim("permission", Permissions.AreaExpertApprove);
        var canWithdraw = expert.UserId == userId && expert.ApprovedAt is null;
        if (!canManage && !canWithdraw) return Forbid();

        // A manager rejecting someone else's still-pending request is the "final
        // decision" the requester should be notified about — not a self-withdrawal,
        // and not revoking access already granted to an approved expert.
        var isRejection = canManage && expert.UserId != userId && expert.ApprovedAt is null;

        db.AreaExperts.Remove(expert);
        await db.SaveChangesAsync();

        if (isRejection && !string.IsNullOrEmpty(expert.User.Email))
        {
            try
            {
                var (subject, html) = EmailTemplates.AreaExpertRejected(expert.Area.Name);
                await emailSender.SendAsync(expert.User.Email, subject, html);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to notify {UserEmail} of area-expert rejection for area {AreaId}",
                    expert.User.Email, expert.AreaId);
            }
        }

        return Ok(new { ok = true });
    }

    private IQueryable<AreaExpert> Query() =>
        db.AreaExperts.AsNoTracking()
            .Include(e => e.Area)
            .Include(e => e.User)
            .Include(e => e.ApprovedBy);

    private static AreaExpertDto ToDto(AreaExpert e) =>
        new(e.Id, e.AreaId, e.Area.Name, e.UserId, e.User.Name, e.RequestedAt, e.ApprovedAt, e.ApprovedBy?.Name);
}
