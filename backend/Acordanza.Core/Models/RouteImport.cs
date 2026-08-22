using System.Text.Json.Serialization;

namespace Acordanza.Core.Models;

// What the user handed us to read: a photo of a topo (plan 30) or the URL of a page
// (plan 31). Two artifacts, one pipeline — see RouteImport below for why they share a
// table rather than living in two. Serializes lowercase over the API (DUP-7) — see
// LowerInvariantEnumConverter.
[JsonConverter(typeof(LowerInvariantEnumConverter<RouteImportKind>))]
public enum RouteImportKind
{
    Image,
    Url
}

// Discarded and Failed are deliberately distinct: Discarded is the reader saying
// "this isn't a usable topo" / "this page isn't about a climbing route" — a real
// answer, no retry needed unless the user disagrees — while Failed is the pipeline
// breaking (fetch timeout, Foundry error, poison message, blob gone), which *is*
// worth retrying. Collapsing them would tell the submitter "we couldn't read it"
// when the truth is "we never looked." Serializes lowercase over the API (DUP-7) —
// see LowerInvariantEnumConverter.
[JsonConverter(typeof(LowerInvariantEnumConverter<RouteImportStatus>))]
public enum RouteImportStatus
{
    Pending,
    Completed,
    Discarded,
    Failed
}

// One request to read a route off something the user supplied: an uploaded topo photo
// (Kind == Image, plan 30) or a web page they pasted (Kind == Url, plan 31).
//
// The row is the keystone of the feature, not accounting: enrichment can get away
// with a null result because the route still exists and an admin pressed the button
// while looking at it, but here the submission *is* the entire artifact. A user who waits
// two minutes for silence has been given a broken feature — so every run, including
// every failure, leaves a row the submitter can see with a reason in Spanish.
//
// One table, three nullable columns, rather than table-per-hierarchy (plan 31 §1): TPH
// would buy compile-time separation at the price of a discriminator column EF manages,
// two DbSets, and two shapes for every read DTO and every page that lists imports, while
// what actually varies is three nullable strings. The invariant TPH would enforce for
// free is instead a check constraint in AppDbContext, which is one line and visible in
// the schema.
public class RouteImport
{
    public int Id { get; set; }

    public RouteImportKind Kind { get; set; }

    // Kind == Image only. The uploaded photo lives under the "_imports/" prefix of the
    // topos container until a successful run promotes it to the route's own prefix
    // (RouteImportService), at which point this is rewritten to the promoted name so the
    // row still points at a live blob. Blob Storage has no rename, so "promote" is really
    // download → re-upload → delete.
    public string? BlobName { get; set; }
    public string? ContentType { get; set; }

    // The uploaded file's name for Kind == Image; null for Kind == Url, where SourceUrl
    // is what the list shows instead.
    public string? FileName { get; set; }

    // Kind == Url only: the page the submitter pasted, canonicalised at submission so a
    // re-paste differing by a tracking parameter short-circuits against the same row.
    public string? SourceUrl { get; set; }

    // Given by the submitter in the form, never inferred from the artifact (plan 30's
    // second decision, which plan 31 §2 keeps for the Url kind too): ClimbingRoute.AreaId
    // is a required FK into a tree, and a route filed under the wrong sector is worse than
    // no route at all.
    public int AreaId { get; set; }
    public Area Area { get; set; } = null!;

    // SubmittedBy, not UploadedBy: nobody uploads a URL, and the column has to name both
    // kinds honestly.
    public string SubmittedByUserId { get; set; } = null!;
    public ApplicationUser SubmittedBy { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public RouteImportStatus Status { get; set; }

    // User-facing, and therefore Spanish — like every { error } payload the frontend
    // can surface. Carries the discard reason, or the grade disagreement note when an
    // existing route was matched.
    public string? Outcome { get; set; }

    public int? RouteId { get; set; }
    public ClimbingRoute? Route { get; set; }

    // false ⇒ the reading matched a route that already existed, and only the topo and/or
    // the link were attached.
    public bool RouteWasCreated { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }
}
