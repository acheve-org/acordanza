namespace Acordanza.Api.Dtos;

public record ContributionDto(
    int Id,
    string Name,
    // Routes: "Peña Rúa › Sector 1"; areas: their own name path; openers: null.
    string? Detail,
    DateTimeOffset CreatedAt,
    string ValidationStatus,
    DateTimeOffset? DecidedAt,
    string? DecidedByName,
    string? RejectionReason);

public record PagedContributionsResponse(
    List<ContributionDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
