namespace Acordanza.Api.Dtos;

public record FirstAscensionistDto(int Id, string Name, string? UserId, string? LinkedUserName);

public record CreateFirstAscensionistRequest(string Name);

public record UpdateFirstAscensionistRequest(string Name);

public record FirstAscensionistLinkRequestDto(
    int Id,
    int FirstAscensionistId,
    string FirstAscensionistName,
    string UserId,
    string UserName,
    DateTimeOffset RequestedAt,
    string Status,
    DateTimeOffset? DecidedAt,
    string? DecidedByName,
    string? RejectionReason);

public record CreateFirstAscensionistLinkRequest(int FirstAscensionistId);

public record RejectFirstAscensionistLinkRequest(string Reason);
