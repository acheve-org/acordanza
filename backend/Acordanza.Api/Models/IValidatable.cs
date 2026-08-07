namespace Acordanza.Api.Models;

public interface IValidatable
{
    DateTimeOffset? ValidatedAt { get; set; }
    string? ValidatedByUserId { get; set; }
}
