using System.Linq.Expressions;

namespace Acordanza.Core.Models;

/// <summary>
/// The one visibility rule for browse/list endpoints outside "Mi acordanza": visible(x) ⟺
/// x.ValidationStatus == Approved || x.AddedByUserId == callerId. Detail-by-id/slug endpoints
/// (GET /routes/{id}, /first-ascensionists/by-slug/{slug}, /areas/by-slug-path/*) are NOT gated by this —
/// only lists are. See plan 11 (.claude/plans/11-show-only-validated-items.md) for the rationale.
/// </summary>
public static class ValidationVisibility
{
    public static Expression<Func<ClimbingRoute, bool>> Routes(string userId) =>
        r => r.ValidationStatus == ReviewStatus.Approved || r.AddedByUserId == userId;

    public static Expression<Func<Area, bool>> Areas(string userId) =>
        a => a.ValidationStatus == ReviewStatus.Approved || a.AddedByUserId == userId;

    public static Expression<Func<FirstAscensionist, bool>> FirstAscensionists(string userId) =>
        f => f.ValidationStatus == ReviewStatus.Approved || f.AddedByUserId == userId;
}
