using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Acordanza.Api.Dtos;
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
            .Where(e => e.Status == ReviewStatus.Pending)
            .OrderBy(e => e.RequestedAt)
            .ToListAsync();
        return Ok(experts.Select(ToDto).ToList());
    }

    [Authorize(Policy = Permissions.AreaExpertRequest)]
    [HttpPost]
    [ProducesResponseType(typeof(AreaExpertDto), 200)]
    [ProducesResponseType(typeof(ApiError), 400)]
    public async Task<ActionResult<AreaExpertDto>> RequestExpert(CreateAreaExpertRequest request)
    {
        var area = await db.Areas.FindAsync(request.AreaId);
        if (area is null) return BadRequest(new ApiError("el sector no existe"));
        if (area.ParentAreaId is not null) return BadRequest(new ApiError("solo los sectores principales admiten expertos"));

        var userId = userManager.GetUserId(User)!;
        var alreadyActive = await db.AreaExperts.AnyAsync(e =>
            e.AreaId == request.AreaId &&
            e.UserId == userId &&
            e.Status != ReviewStatus.Rejected);
        if (alreadyActive)
        {
            return BadRequest(new ApiError("ya has solicitado ser experto de este sector"));
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
            return BadRequest(new ApiError("ya has solicitado ser experto de este sector"));
        }

        var saved = await Query().FirstAsync(e => e.Id == expert.Id);
        return Ok(ToDto(saved));
    }

    [Authorize(Policy = Permissions.AreaExpertApprove)]
    [HttpPost("{id:int}/approve")]
    [ProducesResponseType(typeof(AreaExpertDto), 200)]
    [ProducesResponseType(typeof(ApiError), 400)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<ActionResult<AreaExpertDto>> Approve(int id)
    {
        var approverId = userManager.GetUserId(User)!;

        var outcome = await ReviewRequestService.ApproveAsync(db.AreaExperts, id, approverId);
        if (outcome != DecisionOutcome.Applied)
        {
            return outcome == DecisionOutcome.AlreadyResolved
                ? BadRequest(new ApiError("ya se ha resuelto esta solicitud"))
                : NotFound(new ApiError("No encontrado"));
        }

        var saved = await Query().FirstAsync(e => e.Id == id);
        await NotifyDecisionAsync(saved, approved: true, reason: null);

        return Ok(ToDto(saved));
    }

    [Authorize(Policy = Permissions.AreaExpertApprove)]
    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(typeof(AreaExpertDto), 200)]
    [ProducesResponseType(typeof(ApiError), 400)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<ActionResult<AreaExpertDto>> Reject(int id, RejectAreaExpertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new ApiError("el motivo es obligatorio"));
        }

        var approverId = userManager.GetUserId(User)!;
        var reason = request.Reason.Trim();

        var outcome = await ReviewRequestService.RejectAsync(db.AreaExperts, id, approverId, reason);
        if (outcome != DecisionOutcome.Applied)
        {
            return outcome == DecisionOutcome.AlreadyResolved
                ? BadRequest(new ApiError("ya se ha resuelto esta solicitud"))
                : NotFound(new ApiError("No encontrado"));
        }

        var saved = await Query().FirstAsync(e => e.Id == id);
        await NotifyDecisionAsync(saved, approved: false, reason);

        return Ok(ToDto(saved));
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<ActionResult> Cancel(int id)
    {
        var userId = userManager.GetUserId(User)!;

        var outcome = await ReviewRequestService.CancelAsync(db.AreaExperts, id, userId);
        if (outcome != DecisionOutcome.Applied)
        {
            return outcome == DecisionOutcome.AlreadyResolved ? Forbid() : NotFound(new ApiError("No encontrado"));
        }

        return Ok(new { ok = true });
    }

    // Best-effort email notification shared by Approve and Reject, so the two
    // decisions can't drift in how they notify the requester — same pattern
    // as FirstAscensionistLinksController.
    private async Task NotifyDecisionAsync(AreaExpert expert, bool approved, string? reason)
    {
        if (string.IsNullOrEmpty(expert.User.Email)) return;

        await Notifications.BestEffortAsync(logger, async () =>
        {
            var frontendBaseUrl = configuration["Frontend:BaseUrl"]
                ?? throw new InvalidOperationException("Missing Frontend:BaseUrl configuration");
            var (subject, html) = approved
                ? EmailTemplates.AreaExpertApproved(expert.Area.Name, frontendBaseUrl)
                : EmailTemplates.AreaExpertRejected(expert.Area.Name, reason!, frontendBaseUrl);
            await emailSender.SendAsync(expert.User.Email, subject, html);
        }, "Failed to notify {UserEmail} of {Decision} area-expert request for area {AreaId}",
            expert.User.Email, approved ? "approved" : "rejected", expert.AreaId);
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
