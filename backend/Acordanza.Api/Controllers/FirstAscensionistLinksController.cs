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
[Route("api/first-ascensionist-links")]
public class FirstAscensionistLinksController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    IConfiguration configuration,
    ILogger<FirstAscensionistLinksController> logger) : ControllerBase
{
    [HttpGet("mine")]
    public async Task<ActionResult<List<FirstAscensionistLinkRequestDto>>> GetMine()
    {
        var userId = userManager.GetUserId(User)!;
        var links = await Query().Where(l => l.UserId == userId).OrderByDescending(l => l.RequestedAt).ToListAsync();
        return Ok(links.Select(ToDto).ToList());
    }

    [Authorize(Policy = Permissions.FirstAscensionistLinkApprove)]
    [HttpGet("pending")]
    public async Task<ActionResult<List<FirstAscensionistLinkRequestDto>>> GetPending()
    {
        var links = await Query()
            .Where(l => l.Status == ReviewStatus.Pending)
            .OrderBy(l => l.RequestedAt)
            .ToListAsync();
        return Ok(links.Select(ToDto).ToList());
    }

    [Authorize(Policy = Permissions.FirstAscensionistLinkRequest)]
    [HttpPost]
    public async Task<ActionResult<FirstAscensionistLinkRequestDto>> RequestLink(CreateFirstAscensionistLinkRequest request)
    {
        var firstAscensionist = await db.FirstAscensionists.FindAsync(request.FirstAscensionistId);
        if (firstAscensionist is null) return BadRequest(new { error = "el aperturista no existe" });
        if (firstAscensionist.UserId is not null)
        {
            return BadRequest(new { error = "este aperturista ya está vinculado a una cuenta" });
        }

        var userId = userManager.GetUserId(User)!;
        var alreadyPending = await db.FirstAscensionistLinkRequests.AnyAsync(l =>
            l.FirstAscensionistId == request.FirstAscensionistId &&
            l.UserId == userId &&
            l.Status == ReviewStatus.Pending);
        if (alreadyPending)
        {
            return BadRequest(new { error = "ya has solicitado este vínculo" });
        }

        var link = new FirstAscensionistLinkRequest { FirstAscensionistId = request.FirstAscensionistId, UserId = userId };
        db.FirstAscensionistLinkRequests.Add(link);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Lost a race against another pending request for the same
            // (FirstAscensionistId, UserId) pair — the AnyAsync check above
            // passed, but the unique filtered index only lets one insert through.
            return BadRequest(new { error = "ya has solicitado este vínculo" });
        }

        var saved = await Query().FirstAsync(l => l.Id == link.Id);
        return Ok(ToDto(saved));
    }

    [Authorize(Policy = Permissions.FirstAscensionistLinkApprove)]
    [HttpPost("{id:int}/approve")]
    public async Task<ActionResult<FirstAscensionistLinkRequestDto>> Approve(int id)
    {
        var approverId = userManager.GetUserId(User)!;

        await using var transaction = await db.Database.BeginTransactionAsync();

        // Same atomic-UPDATE race guard as AreaExpertsController.Approve: only the
        // request that still finds Status == Pending when the UPDATE runs is
        // affected, so a double-click (or two reviewers approving at once) can't
        // process the same request twice.
        var outcome = await ReviewRequestService.ApproveAsync(db.FirstAscensionistLinkRequests, id, approverId);
        if (outcome != DecisionOutcome.Applied)
        {
            return outcome == DecisionOutcome.AlreadyResolved
                ? BadRequest(new { error = "ya se ha resuelto esta solicitud" })
                : NotFound(new { error = "No encontrado" });
        }

        var link = await Query().FirstAsync(l => l.Id == id);

        // A second atomic UPDATE, conditioned on the target still being unlinked —
        // guards against it having been claimed by a different, already-approved
        // competing request between the two operations. This is the genuinely
        // different shape that keeps this Approve inline rather than going
        // through the shared service.
        var linked = await db.FirstAscensionists
            .Where(f => f.Id == link.FirstAscensionistId && f.UserId == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(f => f.UserId, link.UserId));

        if (linked == 0)
        {
            // Rolling back would leave this request "Pending" forever: it can never
            // be approved (the target is already linked to someone else), yet its
            // unique filtered index would still block the requester from submitting
            // a fresh one. Auto-resolve it as rejected instead, so it's not stuck.
            // Status is already Approved at this point (the update above), not
            // Pending, so this can't go through ReviewRequestService.RejectAsync.
            const string reason = "Este aperturista ya ha sido vinculado a otra cuenta.";
            await db.FirstAscensionistLinkRequests
                .Where(l => l.Id == id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(l => l.Status, ReviewStatus.Rejected)
                    .SetProperty(l => l.DecidedAt, DateTimeOffset.UtcNow)
                    .SetProperty(l => l.DecidedByUserId, approverId)
                    .SetProperty(l => l.RejectionReason, reason));
            await transaction.CommitAsync();

            var autoRejected = await Query().FirstAsync(l => l.Id == id);
            await NotifyDecisionAsync(autoRejected, approved: false, reason);

            return BadRequest(new { error = "este aperturista ya está vinculado a otra cuenta; la solicitud se ha marcado como rechazada" });
        }

        await transaction.CommitAsync();
        await NotifyDecisionAsync(link, approved: true, reason: null);

        return Ok(ToDto(link));
    }

    [Authorize(Policy = Permissions.FirstAscensionistLinkApprove)]
    [HttpPost("{id:int}/reject")]
    public async Task<ActionResult<FirstAscensionistLinkRequestDto>> Reject(int id, RejectFirstAscensionistLinkRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "el motivo es obligatorio" });
        }

        var approverId = userManager.GetUserId(User)!;
        var reason = request.Reason.Trim();

        var outcome = await ReviewRequestService.RejectAsync(db.FirstAscensionistLinkRequests, id, approverId, reason);
        if (outcome != DecisionOutcome.Applied)
        {
            return outcome == DecisionOutcome.AlreadyResolved
                ? BadRequest(new { error = "ya se ha resuelto esta solicitud" })
                : NotFound(new { error = "No encontrado" });
        }

        var link = await Query().FirstAsync(l => l.Id == id);
        await NotifyDecisionAsync(link, approved: false, reason);

        return Ok(ToDto(link));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Cancel(int id)
    {
        var userId = userManager.GetUserId(User)!;

        var outcome = await ReviewRequestService.CancelAsync(db.FirstAscensionistLinkRequests, id, userId);
        if (outcome != DecisionOutcome.Applied)
        {
            return outcome == DecisionOutcome.AlreadyResolved ? Forbid() : NotFound(new { error = "No encontrado" });
        }

        return Ok(new { ok = true });
    }

    // Best-effort email notification shared by Approve (including its
    // auto-reject-on-conflict path) and Reject, so the two decisions can't
    // drift in how they notify the requester.
    private async Task NotifyDecisionAsync(FirstAscensionistLinkRequest link, bool approved, string? reason)
    {
        if (string.IsNullOrEmpty(link.User.Email)) return;

        await Notifications.BestEffortAsync(logger, async () =>
        {
            var frontendBaseUrl = configuration["Frontend:BaseUrl"]
                ?? throw new InvalidOperationException("Missing Frontend:BaseUrl configuration");
            var (subject, html) = approved
                ? EmailTemplates.FirstAscensionistLinkApproved(link.FirstAscensionist.Name, frontendBaseUrl)
                : EmailTemplates.FirstAscensionistLinkRejected(link.FirstAscensionist.Name, reason!, frontendBaseUrl);
            await emailSender.SendAsync(link.User.Email, subject, html);
        }, "Failed to notify user {UserId} of {Decision} first-ascensionist link {LinkId}",
            link.UserId, approved ? "approved" : "rejected", link.Id);
    }

    private IQueryable<FirstAscensionistLinkRequest> Query() =>
        db.FirstAscensionistLinkRequests.AsNoTracking()
            .Include(l => l.FirstAscensionist)
            .Include(l => l.User)
            .Include(l => l.DecidedBy);

    private static FirstAscensionistLinkRequestDto ToDto(FirstAscensionistLinkRequest l) => new(
        l.Id, l.FirstAscensionistId, l.FirstAscensionist.Name,
        l.UserId, l.User.Name,
        l.RequestedAt, l.Status.ToString().ToLowerInvariant(),
        l.DecidedAt, l.DecidedBy?.Name, l.RejectionReason);
}
