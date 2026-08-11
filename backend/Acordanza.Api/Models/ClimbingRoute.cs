namespace Acordanza.Api.Models;

public class ClimbingRoute : IValidatable
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int AreaId { get; set; }
    public Area Area { get; set; } = null!;
    public required string Grade { get; set; }
    public string? Orientation { get; set; }
    public string? Gear { get; set; }
    public string Description { get; set; } = "";
    public int Length { get; set; }
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
