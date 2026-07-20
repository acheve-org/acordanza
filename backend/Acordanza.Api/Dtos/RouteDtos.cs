namespace Acordanza.Api.Dtos;

public record RouteDto(
    int Id,
    string Name,
    string Crag,
    string Grade,
    string GradeSystem,
    string Type,
    string Description,
    string Tags,
    string FirstAscent,
    string AddedBy,
    DateTimeOffset CreatedAt,
    int TickCount,
    double? AvgRating
);

public record RouteDetailDto(
    int Id,
    string Name,
    string Crag,
    string Grade,
    string GradeSystem,
    string Type,
    string Description,
    string Tags,
    string FirstAscent,
    string AddedBy,
    DateTimeOffset CreatedAt,
    List<TickDto> Ticks
);

public record UpsertRouteRequest(
    string Name,
    string Crag,
    string Grade,
    string? GradeSystem,
    string? Type,
    string? Description,
    string? Tags,
    string? FirstAscent
);
