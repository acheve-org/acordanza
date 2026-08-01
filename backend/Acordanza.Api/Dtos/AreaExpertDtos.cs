namespace Acordanza.Api.Dtos;

public record AreaExpertDto(
    int Id,
    int AreaId,
    string AreaName,
    string UserId,
    string UserName,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ApprovedAt,
    string? ApprovedByName);

public record CreateAreaExpertRequest(int AreaId);
