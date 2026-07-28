namespace Acordanza.Api.Models;

public class RouteOpener
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
}
