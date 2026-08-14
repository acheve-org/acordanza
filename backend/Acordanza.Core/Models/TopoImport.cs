namespace Acordanza.Core.Models;

// One upload of a topo photo the vision pipeline is asked to read (plan 30).
//
// The row is the keystone of the feature, not accounting: enrichment can get away
// with a null result because the route still exists and an admin pressed the button
// while looking at it, but here the upload *is* the entire artifact. A user who waits
// two minutes for silence has been given a broken feature — so every run, including
// every failure, leaves a row the uploader can see with a reason in Spanish.
public class TopoImport
{
    public int Id { get; set; }

    // The uploaded image. Lives under the "_imports/" prefix of the topos container
    // until a successful run promotes it to the route's own prefix (RouteImportService),
    // at which point this is rewritten to the promoted name so the row still points at
    // a live blob. Blob Storage has no rename, so "promote" is really
    // download → re-upload → delete.
    public required string BlobName { get; set; }
    public required string ContentType { get; set; }
    public required string FileName { get; set; }

    // Given by the uploader in the form, never inferred from the photo (plan 30's
    // second decision): ClimbingRoute.AreaId is a required FK into a tree, and a route
    // filed under the wrong sector is worse than no route at all.
    public int AreaId { get; set; }
    public Area Area { get; set; } = null!;

    public string UploadedByUserId { get; set; } = null!;
    public ApplicationUser UploadedBy { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public TopoImportStatus Status { get; set; }

    // User-facing, and therefore Spanish — like every { error } payload the frontend
    // can surface. Carries the discard reason, or the grade disagreement note when an
    // existing route was matched.
    public string? Outcome { get; set; }

    public int? RouteId { get; set; }
    public ClimbingRoute? Route { get; set; }

    // false ⇒ the extraction matched a route that already existed, and only the topo
    // was attached.
    public bool RouteWasCreated { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }
}

// Discarded and Failed are deliberately distinct: Discarded is the model saying
// "this isn't a usable topo" — a real answer, no retry needed unless the user
// disagrees — while Failed is the pipeline breaking (Foundry timeout, poison
// message, blob gone), which *is* worth retrying. Collapsing them would tell the
// uploader "we couldn't read it" when the truth is "we never looked."
public enum TopoImportStatus
{
    Pending,
    Completed,
    Discarded,
    Failed
}
