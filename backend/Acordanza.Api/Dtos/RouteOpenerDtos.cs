namespace Acordanza.Api.Dtos;

public record RouteOpenerDto(int Id, string Name, string? UserId, string? UserName);

public record CreateRouteOpenerRequest(string Name, string? UserId);

public record UserSummaryDto(string Id, string UserName);
