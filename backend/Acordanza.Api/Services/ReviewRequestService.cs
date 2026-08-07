using Acordanza.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Services;

public enum DecisionOutcome
{
    Applied,
    AlreadyResolved,
    NotFound
}

// The atomic-UPDATE/DELETE primitive shared by every self-service
// request/approve/reject/cancel flow (AreaExpert, FirstAscensionistLinkRequest):
// a single conditional statement scoped to Status == Pending, so a double-click
// or two reviewers acting concurrently can't process the same request twice —
// the loser gets 0 rows affected, disambiguated below into AlreadyResolved
// (the id exists but isn't Pending, or isn't owned by the caller for Cancel)
// vs NotFound. Callers still own the transition-specific parts (transactions,
// side effects, notifications) — this only replaces the repeated UPDATE/DELETE
// + disambiguation boilerplate.
public static class ReviewRequestService
{
    public static async Task<DecisionOutcome> ApproveAsync<T>(DbSet<T> set, int id, string deciderId)
        where T : class, IReviewableRequest
    {
        var decidedAt = DateTimeOffset.UtcNow;
        var updated = await set
            .Where(e => e.Id == id && e.Status == ReviewStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Status, ReviewStatus.Approved)
                .SetProperty(e => e.DecidedAt, decidedAt)
                .SetProperty(e => e.DecidedByUserId, deciderId));

        return await Disambiguate(set, id, updated);
    }

    public static async Task<DecisionOutcome> RejectAsync<T>(DbSet<T> set, int id, string deciderId, string reason)
        where T : class, IReviewableRequest
    {
        var updated = await set
            .Where(e => e.Id == id && e.Status == ReviewStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Status, ReviewStatus.Rejected)
                .SetProperty(e => e.DecidedAt, DateTimeOffset.UtcNow)
                .SetProperty(e => e.DecidedByUserId, deciderId)
                .SetProperty(e => e.RejectionReason, reason));

        return await Disambiguate(set, id, updated);
    }

    public static async Task<DecisionOutcome> CancelAsync<T>(DbSet<T> set, int id, string ownerUserId)
        where T : class, IReviewableRequest
    {
        var deleted = await set
            .Where(e => e.Id == id && e.UserId == ownerUserId && e.Status == ReviewStatus.Pending)
            .ExecuteDeleteAsync();

        return await Disambiguate(set, id, deleted);
    }

    private static async Task<DecisionOutcome> Disambiguate<T>(DbSet<T> set, int id, int rowsAffected)
        where T : class, IReviewableRequest
    {
        if (rowsAffected > 0) return DecisionOutcome.Applied;
        var exists = await set.AnyAsync(e => e.Id == id);
        return exists ? DecisionOutcome.AlreadyResolved : DecisionOutcome.NotFound;
    }
}
