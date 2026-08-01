namespace Acordanza.Api.Models;

public class AreaExpert
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public Area Area { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedByUserId { get; set; }
    public ApplicationUser? ApprovedBy { get; set; }
}
