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
    int Length,
    string? ObligatoryGrade,
    string? RecommendedGear,
    string? OptionalGear,
    List<string> Links,
    List<FirstAscensionistDto> FirstAscensionists,
    string AddedBy,
    DateTimeOffset CreatedAt,
    bool HasTick,
    bool HasTopos,
    string ValidationStatus,
    DateTimeOffset? DecidedAt,
    string? DecidedByName,
    string? RejectionReason,
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
    int Length,
    string? ObligatoryGrade,
    string? RecommendedGear,
    string? OptionalGear,
    List<string> Links,
    List<FirstAscensionistDto> FirstAscensionists,
    string AddedBy,
    DateTimeOffset CreatedAt,
    bool HasTick,
    List<TopoDto> Topos,
    string ValidationStatus,
    DateTimeOffset? DecidedAt,
    string? DecidedByName,
    string? RejectionReason,
    bool CanValidate,
    DateTimeOffset? EnrichedAt
);

public record UpsertRouteRequest(
    string Name,
    int AreaId,
    string Grade,
    string? Orientation,
    string? Gear,
    string? Description,
    int Length,
    string? ObligatoryGrade,
    string? RecommendedGear,
    string? OptionalGear,
    List<string>? Links,
    List<int>? FirstAscensionistIds
);

// The review queue's view of a not-yet-decided route — GET /routes/pending.
public record RoutePendingDto(int Id, string Name, string AreaPath, string Grade, DateTimeOffset CreatedAt, string AddedByName);
