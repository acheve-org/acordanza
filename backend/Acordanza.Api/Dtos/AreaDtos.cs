namespace Acordanza.Api.Dtos;

public record GeoPointDto(double Lat, double Lng);

public record AreaDto(
    int Id,
    string Name,
    string Slug,
    int? ParentAreaId,
    List<GeoPointDto> Polygon,
    double? CentroidLat,
    double? CentroidLng,
    string ValidationStatus,
    DateTimeOffset? DecidedAt,
    string? DecidedByName,
    string? RejectionReason);

public record CreateAreaRequest(string Name, int? ParentAreaId);

public record UpdateAreaRequest(string Name, int? ParentAreaId);

public record UpdateAreaCoordinatesRequest(List<GeoPointDto> Polygon);

// Slim projection for the areas-management tree: no polygon/vertex data, per issue #52.
// HasPolygon is just the "does a boundary exist" bit the tree needs to label its
// draw/edit-polygon button — the vertices themselves are only fetched once the polygon
// modal actually opens.
public record AreaNodeDto(int Id, string Name, string Slug, bool HasChildren, bool HasPolygon);

public record PagedAreaNodesResponse(List<AreaNodeDto> Items, int TotalCount, int Page, int PageSize);

// Area detail page (issue #105) — resolved via GET /areas/by-slug-path/{*path}.
public record AreaAncestorDto(int Id, string Name, string Slug);

public record AreaChildDto(int Id, string Name, string Slug, bool HasChildren);

public record AreaDetailDto(
    int Id,
    string Name,
    string Slug,
    int? ParentAreaId,
    List<AreaAncestorDto> Ancestors,
    List<AreaChildDto> Children,
    string ValidationStatus,
    DateTimeOffset? DecidedAt,
    string? DecidedByName,
    string? RejectionReason,
    bool CanManageMedia,
    List<TopoDto> Media);
