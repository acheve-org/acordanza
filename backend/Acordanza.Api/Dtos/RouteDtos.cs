namespace Acordanza.Api.Dtos;

// Read-side only, and deliberately asymmetric with UpsertRouteRequest.Links, which stays a
// bare string array: anything arriving over HTTP is human-entered by definition, so a
// request body has no business asserting its own provenance — accepting one would make
// Link.Source forgeable. Source is the lowercased LinkSource name ("human"/"enrichment"),
// same convention as ValidationStatus.
public record RouteLinkDto(string Url, string Source);

public record RouteDto(
    int Id,
    string Name,
    string Slug,
    int AreaId,
    string AreaName,
    string AreaPath,
    string AreaSlugPath,
    string? Grade,
    string? AidGrade,
    string? Orientation,
    string? Gear,
    string Description,
    int? Length,
    string? ObligatoryGrade,
    string? RecommendedGear,
    string? OptionalGear,
    List<RouteLinkDto> Links,
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
    string Slug,
    int AreaId,
    string AreaName,
    string AreaPath,
    string AreaSlugPath,
    string? Grade,
    string? AidGrade,
    string? Orientation,
    string? Gear,
    string Description,
    int? Length,
    string? ObligatoryGrade,
    string? RecommendedGear,
    string? OptionalGear,
    List<RouteLinkDto> Links,
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
    string? Grade,
    string? AidGrade,
    string? Orientation,
    string? Gear,
    string? Description,
    int? Length,
    string? ObligatoryGrade,
    string? RecommendedGear,
    string? OptionalGear,
    List<string>? Links,
    List<int>? FirstAscensionistIds
);

// The review queue's view of a not-yet-decided route — GET /routes/pending. Slug/
// AreaSlugPath let PendingTasksPage navigate straight to the shared route-detail
// overlay (plan 21) without a second round-trip.
public record RoutePendingDto(
    int Id, string Name, string Slug, string AreaPath, string AreaSlugPath, string? Grade, DateTimeOffset CreatedAt, string AddedByName);
