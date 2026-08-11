namespace Acordanza.Core.Models;

public class FirstAscensionistLinkRequest : IReviewableRequest
{
    public int Id { get; set; }
    public int FirstAscensionistId { get; set; }
    public FirstAscensionist FirstAscensionist { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedByUserId { get; set; }
    public ApplicationUser? DecidedBy { get; set; }
    public string? RejectionReason { get; set; }
}
