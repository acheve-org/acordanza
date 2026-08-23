using System.Text.Json.Serialization;

namespace Acordanza.Core.Models;

// Shared by every self-service request/approve/reject flow (AreaExpert,
// FirstAscensionistLinkRequest — see IReviewableRequest) and every editorial
// validation lifecycle (ClimbingRoute, Area, FirstAscensionist — see IValidatable).
// Serializes lowercase over the API (DUP-7) — see LowerInvariantEnumConverter.
[JsonConverter(typeof(LowerInvariantEnumConverter<ReviewStatus>))]
public enum ReviewStatus
{
    Pending,
    Approved,
    Rejected
}
