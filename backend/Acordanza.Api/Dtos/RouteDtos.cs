namespace Acordanza.Api.Dtos;

public record RouteDto(
    int Id,
    string Name,
    int AreaId,
    string AreaName,
    string AreaPath,
    string Grade,
    string? Orientation,
    string? Gear,
    string Description,
    string Tags,
    List<FirstAscensionistDto> FirstAscensionists,
    string AddedBy,
    DateTimeOffset CreatedAt,
    int TickCount,
    double? AvgRating,
    bool HasTopos,
    DateTimeOffset? ValidatedAt,
    string? ValidatedBy
);

public record RouteDetailDto(
    int Id,
    string Name,
    int AreaId,
    string AreaName,
    string AreaPath,
    string Grade,
    string? Orientation,
    string? Gear,
    string Description,
    string Tags,
    List<FirstAscensionistDto> FirstAscensionists,
    string AddedBy,
    DateTimeOffset CreatedAt,
    List<TickDto> Ticks,
    List<TopoDto> Topos,
    DateTimeOffset? ValidatedAt,
    string? ValidatedBy
);

public record UpsertRouteRequest(
    string Name,
    int AreaId,
    string Grade,
    string? Orientation,
    string? Gear,
    string? Description,
    string? Tags,
    List<int>? FirstAscensionistIds
);
