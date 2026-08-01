namespace Acordanza.Api.Dtos;

public record GeoPointDto(double Lat, double Lng);

public record AreaDto(
    int Id,
    string Name,
    int? ParentAreaId,
    List<GeoPointDto> Polygon,
    double? CentroidLat,
    double? CentroidLng);

public record CreateAreaRequest(string Name, int? ParentAreaId);

public record UpdateAreaCoordinatesRequest(List<GeoPointDto> Polygon);
