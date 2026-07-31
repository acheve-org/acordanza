namespace Acordanza.Api.Dtos;

public record UserAccountDto(string Id, string Name, string Email, string Role, bool Pending);

public record PagedUsersResponse(List<UserAccountDto> Items, int TotalCount, int Page, int PageSize);

public record InviteUserRequest(string Email, string Role);
