using Fido2NetLib;

namespace Acordanza.Api.Dtos;

public record LoginRequest(string Email, string Password);

public record LoginResponse(string? Token, string? Username, bool MfaRequired, string? SessionId, AssertionOptions? Options);

public record PasskeyLoginRequest(string SessionId, AuthenticatorAssertionRawResponse AssertionResponse);

public record MeResponse(string Username, string Name, string Role, IReadOnlyList<string> Permissions);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Email, string Token, string NewPassword);

public record AcceptInviteRequest(string Email, string Token, string Name, string Password);
