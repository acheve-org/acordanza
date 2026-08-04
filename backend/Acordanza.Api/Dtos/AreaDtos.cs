namespace Acordanza.Api.Dtos;

public record GeoPointDto(double Lat, double Lng);

public record AreaDto(
    int Id,
    string Name,
    int? ParentAreaId,
    List<GeoPointDto> Polygon,
    double? CentroidLat,
    double? CentroidLng,
    DateTimeOffset? ValidatedAt,
    string? ValidatedBy);

public record CreateAreaRequest(string Name, int? ParentAreaId);

public record UpdateAreaRequest(string Name, int? ParentAreaId);

public record UpdateAreaCoordinatesRequest(List<GeoPointDto> Polygon);

// Slim projection for the areas-management tree: no polygon/vertex data, per issue #52.
public record AreaNodeDto(int Id, string Name, bool HasChildren);

public record PagedAreaNodesResponse(List<AreaNodeDto> Items, int TotalCount, int Page, int PageSize);
