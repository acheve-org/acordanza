namespace Acordanza.Api.Models;

public class Area
{
    public int Id { get; set; }
    public required string Name { get; set; }

    // ASCII, path-safe form of Name, e.g. "pena-rua". Used as an Azure Blob
    // Storage path segment for topo images — see AreasController.CreateArea
    // and RoutesController's blob-path helper.
    public string Slug { get; set; } = "";

    public int? ParentAreaId { get; set; }
    public Area? ParentArea { get; set; }
    public List<Area> Children { get; set; } = [];

    // Vertices of the area's boundary, in order; empty means no boundary has been drawn yet.
    public List<GeoPoint> Polygon { get; set; } = [];

    // Mean of the polygon's vertices, recomputed whenever the polygon is saved (see AreasController).
    public double? CentroidLat { get; set; }
    public double? CentroidLng { get; set; }
}

public class GeoPoint
{
    public double Lat { get; set; }
    public double Lng { get; set; }
}
