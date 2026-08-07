namespace Acordanza.Api.Models;

// Shared by every self-service request/approve/reject flow (AreaExpert,
// FirstAscensionistLinkRequest) — see IReviewableRequest.
public enum ReviewStatus
{
    Pending,
    Approved,
    Rejected
}
