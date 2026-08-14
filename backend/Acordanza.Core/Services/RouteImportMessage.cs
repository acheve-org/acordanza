namespace Acordanza.Core.Services;

// The wire contract for the route-import queue: the API serializes this when
// enqueueing (RouteImportQueue), the worker deserializes it when draining
// (RouteImportQueueProcessor). One field, because everything else — the kind, the
// blob or the URL, the sector, the submitter — is already on the row.
//
// Sibling of EnrichmentMessage, on its own queue — a discriminated union on one queue
// would muddy both, and EnrichmentQueueProcessor treats an undeserializable message as
// poison, which a second shape on the same queue would trip. Two queues also let each
// carry its own visibility timeout. Both import kinds ride *this* one, though: they
// differ only in how the reader turns the artifact into a candidate (plan 31 §2).
public record RouteImportMessage(int ImportId);

// The two names both processes have to agree on for route import (plans 30 and 31).
public static class RouteImportKeys
{
    // The staging area for an uploaded topo: the API writes here, the worker promotes
    // out of it on success (or deletes on a discard) — see plan 30 §2. Same container
    // as the permanent topos, distinct prefix: a second container would need its own
    // Terraform resource, role assignment and ensure-exists call, for no isolation this
    // app needs. Orphans (a crashed run between upload and processing) are swept by an
    // Azure Storage lifecycle rule on this prefix, not by cleanup code. Url imports stage
    // nothing — the artifact is a URL on the row.
    public const string StagingPrefix = "_imports/";

    // DI service key for the route-import QueueClient, in both processes. Keyed rather
    // than a second bare QueueClient registration: the enrichment client is registered
    // unkeyed and already resolved that way by name in both test fixtures, so a second
    // unkeyed one would silently shadow it depending on registration order.
    public const string QueueServiceKey = "route-import";
}
