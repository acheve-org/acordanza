namespace Acordanza.Api.Models;

// Single source for the ClimbingRoute/Area validation lifecycle: an edit clears prior
// validation only when something actually changed, so re-saving a form as-is doesn't
// generate a spurious pending-validation task.
public static class Validation
{
    public static void MarkValidated(IValidatable entity, string userId)
    {
        entity.ValidatedAt = DateTimeOffset.UtcNow;
        entity.ValidatedByUserId = userId;
    }

    public static void InvalidateIfChanged(IValidatable entity, bool changed)
    {
        if (!changed) return;

        entity.ValidatedAt = null;
        entity.ValidatedByUserId = null;
    }
}
