namespace Acordanza.Api.Models;

public class Area
{
    public int Id { get; set; }
    public required string Name { get; set; }

    // ASCII, path-safe form of Name plus an id suffix, e.g. "pena-rua-12".
    // Used as an Azure Blob Storage path segment for topo images — see
    // AreasController.CreateArea and RoutesController's blob-path helper.
    public string Slug { get; set; } = "";

    public int? ParentAreaId { get; set; }
    public Area? ParentArea { get; set; }
    public List<Area> Children { get; set; } = [];
}
