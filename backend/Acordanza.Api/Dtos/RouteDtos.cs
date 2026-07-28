namespace Acordanza.Api.Dtos;

public record RouteDto(
    int Id,
    string Name,
    int CragId,
    string CragName,
    string CragPath,
    string Grade,
    string? Orientation,
    string? Gear,
    string Description,
    string Tags,
    List<RouteOpenerDto> Openers,
    string AddedBy,
    DateTimeOffset CreatedAt,
    int TickCount,
    double? AvgRating
);

public record RouteDetailDto(
    int Id,
    string Name,
    int CragId,
    string CragName,
    string CragPath,
    string Grade,
    string? Orientation,
    string? Gear,
    string Description,
    string Tags,
    List<RouteOpenerDto> Openers,
    string AddedBy,
    DateTimeOffset CreatedAt,
    List<TickDto> Ticks
);

public record UpsertRouteRequest(
    string Name,
    int CragId,
    string Grade,
    string? Orientation,
    string? Gear,
    string? Description,
    string? Tags,
    List<int>? OpenerIds
);
