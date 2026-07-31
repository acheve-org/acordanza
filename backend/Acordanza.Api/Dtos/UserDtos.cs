namespace Acordanza.Api.Dtos;

public record UserAccountDto(string Id, string Name, string Email, string Role);

public record PagedUsersResponse(List<UserAccountDto> Items, int TotalCount, int Page, int PageSize);
