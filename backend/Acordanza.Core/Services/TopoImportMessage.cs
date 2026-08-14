namespace Acordanza.Core.Services;

// The wire contract for the topo-import queue: the API serializes this when
// enqueueing (TopoImportQueue), the worker deserializes it when draining
// (TopoImportQueueProcessor). Sibling of EnrichmentMessage, on its own queue —
// a discriminated union on one queue would muddy both, and EnrichmentQueueProcessor
// treats an undeserializable message as poison, which a second shape on the same
// queue would trip. Two queues also let each carry its own visibility timeout.
public record TopoImportMessage(int ImportId);

// The two names both processes have to agree on for topo import (plan 30).
public static class TopoImportKeys
{
    // The staging area for an uploaded topo: the API writes here, the worker promotes
    // out of it on success (or deletes on a discard) — see plan 30 §2. Same container
    // as the permanent topos, distinct prefix: a second container would need its own
    // Terraform resource, role assignment and ensure-exists call, for no isolation this
    // app needs. Orphans (a crashed run between upload and processing) are swept by an
    // Azure Storage lifecycle rule on this prefix, not by cleanup code.
    public const string StagingPrefix = "_imports/";

    // DI service key for the topo-import QueueClient, in both processes. Keyed rather
    // than a second bare QueueClient registration: the enrichment client is registered
    // unkeyed and already resolved that way by name in both test fixtures, so a second
    // unkeyed one would silently shadow it depending on registration order.
    public const string QueueServiceKey = "topo-import";
}
