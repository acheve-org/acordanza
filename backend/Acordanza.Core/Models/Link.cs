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
    Enrichment,

    // The page a user pasted into /my-acordanza/my-imports (plan 31). Needs no migration:
    // the column is HasConversion<string>().HasMaxLength(20) with a store-side default,
    // so a new value is a code-only change. Note this is the *only* URL a URL import ever
    // writes — never one found on the page, which is the concrete bound on prompt
    // injection plan 31 §5 draws.
    Import
}

public class Link
{
    public int Id { get; set; }
    public int RouteId { get; set; }
    public ClimbingRoute Route { get; set; } = null!;
    public required string Url { get; set; }
    public LinkSource Source { get; set; } = LinkSource.Human;
}
