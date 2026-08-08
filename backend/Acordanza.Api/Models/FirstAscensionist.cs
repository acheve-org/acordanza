namespace Acordanza.Api.Models;

public class FirstAscensionist : IValidatable
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    // Nullable, unlike ClimbingRoute.AddedByUserId — records predating this column have no
    // recoverable creator, and simply never appear in anyone's contributions list.
    public string? AddedByUserId { get; set; }
    public ApplicationUser? AddedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ReviewStatus ValidationStatus { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedByUserId { get; set; }
    public ApplicationUser? DecidedBy { get; set; }
    public string? RejectionReason { get; set; }
}
