namespace Acordanza.Core.Models;

// Who put this link on the route (plan 29 §6). Deliberately provenance only — it
// gates nothing and never touches ValidationStatus; what it buys is that a bad
// enrichment link is *identifiable*: the detail modal badges it, a bad batch is one
// `WHERE "Source" = 'Enrichment'` away from being deleted, and link precision becomes
// countable. Anything arriving over HTTP is Human by definition (RoutesController) —
// the write DTO carries no source field, so it can't be forged.
public enum LinkSource
{
    Human,
    Enrichment
}

public class Link
{
    public int Id { get; set; }
    public int RouteId { get; set; }
    public ClimbingRoute Route { get; set; } = null!;
    public required string Url { get; set; }
    public LinkSource Source { get; set; } = LinkSource.Human;
}
