namespace Acordanza.Api.Dtos;

public record TickDto(
    int Id,
    int RouteId,
    string Climber,
    string Date,
    int? Rating,
    string Notes,
    DateTimeOffset CreatedAt
);

public record CreateTickRequest(string Date, int? Rating, string? Notes);

// One of the current user's own ticks, with just enough of its route's info to
// render/filter a "my ticks" list without a second round-trip per row.
public record MyTickDto(
    int TickId,
    int RouteId,
    string RouteName,
    int AreaId,
    string AreaName,
    string AreaPath,
    string Grade,
    string? Orientation,
    string? Gear,
    int Length,
    string Date,
    int? Rating,
    string Notes,
    DateTimeOffset CreatedAt
);

public record PagedMyTicksResponse(List<MyTickDto> Items, int TotalCount, int Page, int PageSize);
