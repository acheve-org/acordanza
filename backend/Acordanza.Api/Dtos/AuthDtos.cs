using Fido2NetLib;

namespace Acordanza.Api.Dtos;

public record LoginRequest(string Email, string Password);

public record LoginResponse(string? Token, string? Username, bool MfaRequired, string? SessionId, AssertionOptions? Options);

public record PasskeyLoginRequest(string SessionId, AuthenticatorAssertionRawResponse AssertionResponse);

public record MeResponse(string Username);
