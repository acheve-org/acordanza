using Acordanza.Core.Models;

namespace Acordanza.Api.Dtos;

// One row of /my-acordanza/my-imports, for either kind. Status and Kind serialize
// lowercase via the global JsonStringEnumConverter naming policy (Program.cs), same
// wire shape every other enum DTO carries (see ContributionDto); Outcome is already
// Spanish (it's written for this reader — see Acordanza.Core/Models/RouteImport.cs).
//
// FileName and SourceUrl are the two kind-specific fields, exactly as on the row: one is
// null for the other's kind, and the page shows whichever is there as the row's title.
//
// RouteSlug/RouteAreaSlugPath are the two halves of the shareable route URL
// (/routes/{area-slug-path}/{route-slug}, plan 21) so a Completed row links
// straight to what it produced without the page resolving anything itself.
public record RouteImportDto(
    int Id,
    RouteImportKind Kind,
    string? FileName,
    string? SourceUrl,
    int AreaId,
    string AreaNamePath,
    DateTimeOffset CreatedAt,
    RouteImportStatus Status,
    string? Outcome,
    int? RouteId,
    string? RouteName,
    string? RouteSlug,
    string? RouteAreaSlugPath,
    bool RouteWasCreated,
    DateTimeOffset? ProcessedAt);

// The Url kind's request body. The Image kind has no record of its own — it arrives as
// multipart/form-data (file + areaId), which model binding takes as parameters.
public record CreateUrlImportRequest(string? Url, int AreaId);
