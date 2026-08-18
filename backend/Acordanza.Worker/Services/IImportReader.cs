using Acordanza.Core.Models;

namespace Acordanza.Worker.Services;

// The seam between "what the user gave us" and "what we're willing to write" (plan 31 §2).
//
// The two kinds differ only in how they turn an artifact into a candidate: a staged photo
// goes through a gate and a vision extraction, a URL goes through a fetch, a strip-to-text
// and one text extraction plus an image sweep. Everything after that — match or create,
// attach the topo, append the link, write the outcome — is one implementation in
// RouteImportService, which consequently knows nothing about images or HTTP.
public interface IImportReader
{
    RouteImportKind Kind { get; }

    Task<RouteReading> ReadAsync(RouteImport import, CancellationToken cancellationToken);
}

// What a reader is willing to hand the spine. Exactly one of the three outcomes is
// meaningful at a time, and the record forces the reader to say which — the reader is the
// only thing that can tell a real "no" from a broken pipeline, and collapsing the two
// tells the submitter "we couldn't read it" when the truth is "we never looked".
//
//   Route != null                  → we have something to write
//   DiscardReason != null          → a real answer: not a topo, not about a route, …
//   Failed                         → the pipeline broke; a retry is worth something
public record RouteReading(
    RouteCandidate? Route,
    TopoCandidate? Topo,
    string? DiscardReason,
    bool Failed = false)
{
    public static RouteReading Discard(string reason) => new(null, null, reason);

    public static RouteReading Broke(string reason) => new(null, null, reason, Failed: true);
}

// What the pipeline is willing to write, after the reader's acceptance predicate: every
// field non-null and already normalised, so nothing downstream re-checks them.
//
// FirstAscensionists and Description are the Url kind's contribution — a blog post names
// the openers and says what the route is like, which a photo of a topo does not. Both are
// empty for the Image kind. The names are *matched* against the directory, never created
// (plan 31 §5/§8), which happens in the spine rather than here.
// SectorMentioned is a cross-check, not an input: a page naming a crag nowhere in the
// picked sector's breadcrumb gets a note in Outcome, and nothing else happens.
// ObligatoryGrade is the one field here that is optional in the *source* rather than in
// one of the two kinds: both kinds fill it when the artifact printed one (see
// PrintedGrade), and most artifacts don't.
// Grade/AidGrade/Length are all nullable now (plan 34): a human may save a route with none
// of them, so a photo or page that doesn't state one must not be discarded either.
// GradeNote carries PrintedGrade's raw, unreadable input through to the outcome when Grade
// and AidGrade are both null but the source printed *something* in the grade slot — a gap
// the reviewer can fix in one edit rather than a re-import.
public record RouteCandidate(
    string Name,
    string? Grade,
    int? Length,
    string Evidence,
    string Description = "",
    IReadOnlyList<string>? FirstAscensionists = null,
    string? SectorMentioned = null,
    string? ObligatoryGrade = null,
    string? Gear = null,
    string? AidGrade = null,
    string? GradeNote = null);

// An image the reader has already vision-verified as a topo of *this* route. Bytes rather
// than a URL or a blob name, because the two kinds get here by different routes (a staged
// upload, a download off the page) and the spine promotes them identically.
public record TopoCandidate(byte[] Bytes, string ContentType, string FileName);
