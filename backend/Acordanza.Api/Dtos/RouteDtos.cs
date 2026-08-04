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
    bool HasTick,
    bool HasTopos,
    DateTimeOffset? ValidatedAt,
    string? ValidatedBy,
    bool CanValidate
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
    bool HasTick,
    List<TopoDto> Topos,
    DateTimeOffset? ValidatedAt,
    string? ValidatedBy,
    bool CanValidate
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
