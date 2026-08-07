namespace Acordanza.Api.Dtos;

public record FirstAscensionistDto(int Id, string Name, string? UserId, string? LinkedUserName);

// The "Sigue" section's view of a first ascensionist: the plain DTO plus what they've
// opened. Kept separate from FirstAscensionistDto because that one is embedded in every
// route payload, where the extra aggregate columns would be dead weight.
public record FirstAscensionistProfileDto(
    int Id,
    string Name,
    string? UserId,
    string? LinkedUserName,
    int RouteCount,
    List<string> AreaNames);

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
