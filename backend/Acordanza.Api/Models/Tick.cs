namespace Acordanza.Api.Models;

public class Tick
{
    public int Id { get; set; }
    public int RouteId { get; set; }
    public ClimbingRoute Route { get; set; } = null!;
    public string ClimberUserId { get; set; } = null!;
    public ApplicationUser Climber { get; set; } = null!;
    public required string Date { get; set; }
    public int? Rating { get; set; }
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
