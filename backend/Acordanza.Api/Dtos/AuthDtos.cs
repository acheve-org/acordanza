namespace Acordanza.Api.Dtos;

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, string Username);

public record MeResponse(string Username);
