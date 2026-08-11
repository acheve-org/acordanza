namespace Acordanza.Core.Models;

public class Link
{
    public int Id { get; set; }
    public int RouteId { get; set; }
    public ClimbingRoute Route { get; set; } = null!;
    public required string Url { get; set; }
}
