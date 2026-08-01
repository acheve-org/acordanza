namespace Acordanza.Api.Models;

public enum FirstAscensionistLinkStatus
{
    Pending,
    Approved,
    Rejected
}

public class FirstAscensionistLinkRequest
{
    public int Id { get; set; }
    public int FirstAscensionistId { get; set; }
    public FirstAscensionist FirstAscensionist { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    public FirstAscensionistLinkStatus Status { get; set; } = FirstAscensionistLinkStatus.Pending;
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedByUserId { get; set; }
    public ApplicationUser? DecidedBy { get; set; }
    public string? RejectionReason { get; set; }
}
