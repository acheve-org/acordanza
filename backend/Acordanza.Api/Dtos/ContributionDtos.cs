namespace Acordanza.Api.Dtos;

public record ContributionDto(
    int Id,
    string Name,
    // Routes: "Peña Rúa › Sector 1 · 6a"; areas: their own name path; openers: null.
    string? Detail,
    DateTimeOffset CreatedAt,
    string ValidationStatus,
    DateTimeOffset? DecidedAt,
    string? DecidedByName,
    string? RejectionReason);

public record MyContributionsResponse(
    List<ContributionDto> Routes,
    List<ContributionDto> Areas,
    List<ContributionDto> FirstAscensionists);
