namespace Acordanza.Core.Models;

public interface IValidatable
{
    ReviewStatus ValidationStatus { get; set; }
    DateTimeOffset? DecidedAt { get; set; }
    string? DecidedByUserId { get; set; }
    string? RejectionReason { get; set; }
}
