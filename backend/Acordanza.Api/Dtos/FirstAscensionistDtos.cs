namespace Acordanza.Api.Dtos;

public record FirstAscensionistDto(int Id, string Name, string? UserId, string? LinkedUserName);

public record CreateFirstAscensionistRequest(string Name, string? UserId);

public record UserSummaryDto(string Id, string Name);
