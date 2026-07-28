namespace Acordanza.Api.Dtos;

public record CragDto(int Id, string Name, int? ParentCragId);

public record CreateCragRequest(string Name, int? ParentCragId);
