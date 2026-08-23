using Acordanza.Core.Models;

namespace Acordanza.Api.Dtos;

// Read-side only, and deliberately asymmetric with UpsertRouteRequest.Links, which stays a
// bare string array: anything arriving over HTTP is human-entered by definition, so a
// request body has no business asserting its own provenance — accepting one would make
// Link.Source forgeable.
public record RouteLinkDto(string Url, LinkSource Source);

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
    Orientation? Orientation,
    Gear? Gear,
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
    ReviewStatus ValidationStatus,
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
    Orientation? Orientation,
    Gear? Gear,
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
    ReviewStatus ValidationStatus,
    DateTimeOffset? DecidedAt,
    string? DecidedByName,
    string? RejectionReason,
    bool CanValidate,
    DateTimeOffset? EnrichedAt
);

// Orientation/Gear stay plain strings here (not the Orientation/Gear enums the read-side
// DTOs above use) so RoutesController's ValidateOrientation/ValidateGear keep producing
// their existing Spanish error messages on an invalid value — binding straight to the enum
// would instead fail JSON deserialization before the action runs, with ASP.NET Core's
// generic "invalid value" message (DUP-12). See RoutesController.AllowedOrientations/
// AllowedGear, now derived from the enums' own member names rather than duplicated here.
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
