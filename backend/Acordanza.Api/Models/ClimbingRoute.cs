namespace Acordanza.Api.Models;

public class ClimbingRoute
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int CragId { get; set; }
    public Crag Crag { get; set; } = null!;
    public required string Grade { get; set; }
    public string? Orientation { get; set; }
    public string? Gear { get; set; }
    public string Description { get; set; } = "";
    public string Tags { get; set; } = "";
    public string AddedByUserId { get; set; } = null!;
    public ApplicationUser AddedBy { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ValidatedAt { get; set; }
    public string? ValidatedByUserId { get; set; }
    public ApplicationUser? ValidatedBy { get; set; }

    public List<RouteOpener> Openers { get; set; } = [];
    public List<Tick> Ticks { get; set; } = [];
    public List<Topo> Topos { get; set; } = [];
}
