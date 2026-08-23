using Acordanza.Core.Models;

namespace Acordanza.Api.Dtos;

public record AreaExpertDto(
    int Id,
    int AreaId,
    string AreaName,
    string UserId,
    string UserName,
    DateTimeOffset RequestedAt,
    ReviewStatus Status,
    DateTimeOffset? DecidedAt,
    string? DecidedByName,
    string? RejectionReason);

public record CreateAreaExpertRequest(int AreaId);

public record RejectAreaExpertRequest(string Reason);
