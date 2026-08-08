namespace Acordanza.Api.Models;

// Shared by every self-service request/approve/reject flow (AreaExpert,
// FirstAscensionistLinkRequest — see IReviewableRequest) and every editorial
// validation lifecycle (ClimbingRoute, Area, FirstAscensionist — see IValidatable).
public enum ReviewStatus
{
    Pending,
    Approved,
    Rejected
}
