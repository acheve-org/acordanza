using Fido2NetLib;

namespace Acordanza.Api.Dtos;

public record PasskeySummary(int Id, string Name, DateTime CreatedAt);

public record RegisterPasskeyOptionsRequest(string Name);

public record RegisterPasskeyOptionsResponse(string SessionId, CredentialCreateOptions Options);

public record RegisterPasskeyCompleteRequest(string SessionId, AuthenticatorAttestationRawResponse AttestationResponse);
