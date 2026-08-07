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
    IConfiguration configuration,
    ILogger<AreaExpertsController> logger) : ControllerBase
{
    [HttpGet("mine")]
    public async Task<ActionResult<List<AreaExpertDto>>> GetMine()
    {
        var userId = userManager.GetUserId(User)!;
        var experts = await Query().Where(e => e.UserId == userId).OrderByDescending(e => e.RequestedAt).ToListAsync();
        return Ok(experts.Select(ToDto).ToList());
    }

    [Authorize(Policy = Permissions.AreaExpertApprove)]
    [HttpGet("pending")]
    public async Task<ActionResult<List<AreaExpertDto>>> GetPending()
    {
        var experts = await Query()
            .Where(e => e.Status == AreaExpertStatus.Pending)
            .OrderBy(e => e.RequestedAt)
            .ToListAsync();
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
        var alreadyActive = await db.AreaExperts.AnyAsync(e =>
            e.AreaId == request.AreaId &&
            e.UserId == userId &&
            e.Status != AreaExpertStatus.Rejected);
        if (alreadyActive)
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
            // Lost a race against another pending request for the same
            // (AreaId, UserId) pair — the AnyAsync check above passed, but the
            // unique filtered index only lets one insert through.
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
        var decidedAt = DateTimeOffset.UtcNow;

        // A single conditional UPDATE, not a load-check-save round trip: if two
        // reviewers approve concurrently, only the request that still finds
        // Status == Pending when the UPDATE runs affects a row — the loser gets
        // 0 rows affected instead of silently overwriting the first approval.
        var updated = await db.AreaExperts
            .Where(e => e.Id == id && e.Status == AreaExpertStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Status, AreaExpertStatus.Approved)
                .SetProperty(e => e.DecidedAt, decidedAt)
                .SetProperty(e => e.DecidedByUserId, approverId));

        if (updated == 0)
        {
            var exists = await db.AreaExperts.AnyAsync(e => e.Id == id);
            return exists ? BadRequest(new { error = "ya se ha resuelto esta solicitud" }) : NotFound(new { error = "No encontrado" });
        }

        var saved = await Query().FirstAsync(e => e.Id == id);
        await NotifyDecisionAsync(saved, approved: true, reason: null);

        return Ok(ToDto(saved));
    }

    [Authorize(Policy = Permissions.AreaExpertApprove)]
    [HttpPost("{id:int}/reject")]
    public async Task<ActionResult<AreaExpertDto>> Reject(int id, RejectAreaExpertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "el motivo es obligatorio" });
        }

        var approverId = userManager.GetUserId(User)!;
        var reason = request.Reason.Trim();

        var updated = await db.AreaExperts
            .Where(e => e.Id == id && e.Status == AreaExpertStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Status, AreaExpertStatus.Rejected)
                .SetProperty(e => e.DecidedAt, DateTimeOffset.UtcNow)
                .SetProperty(e => e.DecidedByUserId, approverId)
                .SetProperty(e => e.RejectionReason, reason));

        if (updated == 0)
        {
            var exists = await db.AreaExperts.AnyAsync(e => e.Id == id);
            return exists ? BadRequest(new { error = "ya se ha resuelto esta solicitud" }) : NotFound(new { error = "No encontrado" });
        }

        var saved = await Query().FirstAsync(e => e.Id == id);
        await NotifyDecisionAsync(saved, approved: false, reason);

        return Ok(ToDto(saved));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Cancel(int id)
    {
        var userId = userManager.GetUserId(User)!;

        var deleted = await db.AreaExperts
            .Where(e => e.Id == id && e.UserId == userId && e.Status == AreaExpertStatus.Pending)
            .ExecuteDeleteAsync();

        if (deleted == 0)
        {
            var exists = await db.AreaExperts.AnyAsync(e => e.Id == id);
            return exists ? Forbid() : NotFound(new { error = "No encontrado" });
        }

        return Ok(new { ok = true });
    }

    // Best-effort email notification (try/catch + log) shared by Approve and
    // Reject, so the two decisions can't drift in how they notify the
    // requester — same pattern as FirstAscensionistLinksController.
    private async Task NotifyDecisionAsync(AreaExpert expert, bool approved, string? reason)
    {
        if (string.IsNullOrEmpty(expert.User.Email)) return;

        try
        {
            var frontendBaseUrl = configuration["Frontend:BaseUrl"]
                ?? throw new InvalidOperationException("Missing Frontend:BaseUrl configuration");
            var (subject, html) = approved
                ? EmailTemplates.AreaExpertApproved(expert.Area.Name, frontendBaseUrl)
                : EmailTemplates.AreaExpertRejected(expert.Area.Name, reason!, frontendBaseUrl);
            await emailSender.SendAsync(expert.User.Email, subject, html);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to notify {UserEmail} of {Decision} area-expert request for area {AreaId}",
                expert.User.Email, approved ? "approved" : "rejected", expert.AreaId);
        }
    }

    private IQueryable<AreaExpert> Query() =>
        db.AreaExperts.AsNoTracking()
            .Include(e => e.Area)
            .Include(e => e.User)
            .Include(e => e.DecidedBy);

    private static AreaExpertDto ToDto(AreaExpert e) => new(
        e.Id, e.AreaId, e.Area.Name,
        e.UserId, e.User.Name,
        e.RequestedAt, e.Status.ToString().ToLowerInvariant(),
        e.DecidedAt, e.DecidedBy?.Name, e.RejectionReason);
}
