namespace Acordanza.Core.Models;

public class Topo
{
    public int Id { get; set; }
    public int RouteId { get; set; }
    public ClimbingRoute Route { get; set; } = null!;

    // Full path within the "topos" blob container, e.g.
    // "pena-rua/some-route-01.jpg" — see BlobStorageService.
    public required string BlobName { get; set; }
    public required string ContentType { get; set; }
    public required string FileName { get; set; }

    public string UploadedByUserId { get; set; } = null!;
    public ApplicationUser UploadedBy { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
