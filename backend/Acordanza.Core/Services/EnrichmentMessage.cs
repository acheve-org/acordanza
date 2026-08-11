namespace Acordanza.Core.Services;

// The wire contract between the two processes: the API serializes this when
// enqueueing (RouteEnrichmentQueue), the worker deserializes it when draining
// (EnrichmentQueueProcessor). Genuinely shared, unlike the enqueue/drain logic
// either side wraps it in.
public record EnrichmentMessage(int RouteId, bool Force);
