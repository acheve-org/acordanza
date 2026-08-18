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

// GET /areas/tree (plan 35 §1): the whole area tree, flat and geometry-free, for the
// AreaPicker/TreeSelect to hold client-side and browse/filter in memory. RouteCount is
// the area's own routes only — the client rolls up subtree totals itself, since it
// already has the whole tree in hand.
public record AreaTreeNodeDto(int Id, string Name, int? ParentAreaId, int RouteCount, string ValidationStatus);

// Area detail page (issue #105) — resolved via GET /areas/by-slug-path/{*path}.
public record AreaAncestorDto(int Id, string Name, string Slug);

public record AreaChildDto(int Id, string Name, string Slug, bool HasChildren);

// The review queue's view of a not-yet-decided area — GET /areas/pending. NamePath is
// resolved server-side (AreaHierarchyService.NamePath) so the frontend can drop its own
// whole-area-table download just to render a pending area's breadcrumb.
public record AreaPendingDto(int Id, string Name, string NamePath, int? ParentAreaId, DateTimeOffset CreatedAt, string? AddedByName);

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
