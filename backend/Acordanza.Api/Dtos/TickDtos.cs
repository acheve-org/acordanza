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
