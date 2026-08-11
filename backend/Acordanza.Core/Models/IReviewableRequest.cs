namespace Acordanza.Core.Models;

// The shape common to every self-service request/approve/reject flow
// (AreaExpert, FirstAscensionistLinkRequest), letting ReviewRequestService
// operate on either without knowing which one it's handed.
public interface IReviewableRequest
{
    int Id { get; }
    string UserId { get; }
    DateTimeOffset RequestedAt { get; }
    ReviewStatus Status { get; set; }
    DateTimeOffset? DecidedAt { get; set; }
    string? DecidedByUserId { get; set; }
    string? RejectionReason { get; set; }
}
