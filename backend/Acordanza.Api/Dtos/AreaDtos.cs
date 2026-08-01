namespace Acordanza.Api.Dtos;

public record AreaDto(int Id, string Name, int? ParentAreaId);

public record CreateAreaRequest(string Name, int? ParentAreaId);
