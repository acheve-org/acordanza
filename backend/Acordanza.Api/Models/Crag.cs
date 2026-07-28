namespace Acordanza.Api.Models;

public class Crag
{
    public int Id { get; set; }
    public required string Name { get; set; }

    // ASCII, path-safe form of Name plus an id suffix, e.g. "pena-rua-12".
    // Used as an Azure Blob Storage path segment for topo images — see
    // CragsController.CreateCrag and RoutesController's blob-path helper.
    public string Slug { get; set; } = "";

    public int? ParentCragId { get; set; }
    public Crag? ParentCrag { get; set; }
    public List<Crag> Children { get; set; } = [];
}
