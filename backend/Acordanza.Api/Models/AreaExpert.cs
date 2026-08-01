namespace Acordanza.Api.Models;

public enum AreaExpertStatus
{
    Pending,
    Approved,
    Rejected
}

public class AreaExpert
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public Area Area { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    public AreaExpertStatus Status { get; set; } = AreaExpertStatus.Pending;
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedByUserId { get; set; }
    public ApplicationUser? DecidedBy { get; set; }
    public string? RejectionReason { get; set; }
}
