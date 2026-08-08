namespace Acordanza.Api.Dtos;

// Shared by every IValidatable's POST /{id}/reject endpoint (routes, areas, first ascensionists).
public record RejectValidationRequest(string Reason);
