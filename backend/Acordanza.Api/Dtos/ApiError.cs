namespace Acordanza.Api.Dtos;

/// <summary>
/// The single shape every error response body across the API uses, so error responses get an
/// OpenAPI schema (and therefore a typed shape in the frontend's generated client) instead of an
/// anonymous <c>new { error = "..." }</c> object that produces no schema at all.
/// </summary>
public record ApiError(string Error);
