namespace Acordanza.Api.Models;

public class ClimbingRoute
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Crag { get; set; }
    public required string Grade { get; set; }
    public string GradeSystem { get; set; } = "YDS";
    public string Type { get; set; } = "sport";
    public string Description { get; set; } = "";
    public string Tags { get; set; } = "";
    public string FirstAscent { get; set; } = "";
    public string AddedByUserId { get; set; } = null!;
    public ApplicationUser AddedBy { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Tick> Ticks { get; set; } = [];
}
