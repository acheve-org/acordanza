namespace Acordanza.Core.Models;

public class ClimbingRoute : IValidatable
{
    public int Id { get; set; }
    public required string Name { get; set; }

    // ASCII, path-safe form of Name, unique per AreaId (not globally — Name has no
    // uniqueness constraint at all, so a global namespace would hit collisions
    // constantly on common route names). Backs the shareable /routes/{area-slug-path}/
    // {slug} URL, alongside AreaHierarchy.SlugPath(AreaId) for the area segments — see
    // RoutesController.GenerateUniqueRouteSlugAsync. Regenerated whenever Name or AreaId
    // changes, like FirstAscensionist.Slug and unlike Area.Slug: nothing is keyed on this
    // column (route topo blob names are computed fresh from Name at upload time, not from
    // this), so a corrected name/area shouldn't leave a stale URL behind.
    public string Slug { get; set; } = "";

    public int AreaId { get; set; }
    public Area Area { get; set; } = null!;

    // The free-climbing grade, French scale (GradeValidator) — null when nobody has graded
    // the route, or when it isn't free-climbed at all and only AidGrade applies.
    public string? Grade { get; set; }

    // The artificial (aid) grade, A0–A5 / C0–C5 (AidGradeValidator). Independent of Grade,
    // not a replacement for it: «6c/A1» is a route with both, «A2» is a route with only
    // this one, and most routes in this log have neither.
    public string? AidGrade { get; set; }

    public string? Orientation { get; set; }
    public string? Gear { get; set; }
    public string Description { get; set; } = "";

    // Null when unknown. Was a non-nullable int whose 0 the import worker used as an
    // "unknown" sentinel — see the OptionalRouteGradeAndLength migration, which turns
    // those 0s into nulls.
    public int? Length { get; set; }

    public string? ObligatoryGrade { get; set; }
    public string? RecommendedGear { get; set; }
    public string? OptionalGear { get; set; }
    public string AddedByUserId { get; set; } = null!;
    public ApplicationUser AddedBy { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ReviewStatus ValidationStatus { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedByUserId { get; set; }
    public ApplicationUser? DecidedBy { get; set; }
    public string? RejectionReason { get; set; }

    public List<FirstAscensionist> FirstAscensionists { get; set; } = [];
    public List<Tick> Ticks { get; set; } = [];
    public List<Topo> Topos { get; set; } = [];
    public List<Link> Links { get; set; } = [];

    // Set once the enrichment worker has processed this route (Services/RouteEnrichmentService) —
    // the idempotency marker: a redelivered/duplicate queue message finds this set and skips
    // reprocessing unless force is requested. Null means never enriched.
    public DateTimeOffset? EnrichedAt { get; set; }
}
