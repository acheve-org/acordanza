namespace Acordanza.Api.Dtos;

// One row of /my-acordanza/my-imports. Status is lowercased like every other
// enum the frontend narrows to a literal union (see ContributionDto), Outcome is
// already Spanish (it's written for this reader — see Models/TopoImport.cs).
//
// RouteSlug/RouteAreaSlugPath are the two halves of the shareable route URL
// (/routes/{area-slug-path}/{route-slug}, plan 21) so a Completed row links
// straight to what it produced without the page resolving anything itself.
public record TopoImportDto(
    int Id,
    string FileName,
    int AreaId,
    string AreaNamePath,
    DateTimeOffset CreatedAt,
    string Status,
    string? Outcome,
    int? RouteId,
    string? RouteName,
    string? RouteSlug,
    string? RouteAreaSlugPath,
    bool RouteWasCreated,
    DateTimeOffset? ProcessedAt);
