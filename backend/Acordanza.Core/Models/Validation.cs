namespace Acordanza.Core.Models;

// Single source for the ClimbingRoute/Area/FirstAscensionist validation lifecycle: an
// edit clears a prior decision only when something actually changed, so re-saving a
// form as-is doesn't generate a spurious pending-validation task.
public static class Validation
{
    public static void MarkValidated(IValidatable entity, string userId)
    {
        entity.ValidationStatus = ReviewStatus.Approved;
        entity.DecidedAt = DateTimeOffset.UtcNow;
        entity.DecidedByUserId = userId;
        entity.RejectionReason = null;
    }

    public static void MarkRejected(IValidatable entity, string userId, string reason)
    {
        entity.ValidationStatus = ReviewStatus.Rejected;
        entity.DecidedAt = DateTimeOffset.UtcNow;
        entity.DecidedByUserId = userId;
        entity.RejectionReason = reason;
    }

    public static void InvalidateIfChanged(IValidatable entity, bool changed)
    {
        if (!changed) return;

        entity.ValidationStatus = ReviewStatus.Pending;
        entity.DecidedAt = null;
        entity.DecidedByUserId = null;
        entity.RejectionReason = null;
    }
}
