using Acordanza.Worker.Services;
using Shouldly;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// Plan 30 §3 — unit-tier (no Docker, no network, no Foundry): the deterministic half of
// the route-import pipeline, exactly as EnrichmentGroundingTests pins enrichment's.
//
// TopoImageReader.Accept is where every constraint on what the model returned actually
// lives, and it has to: structured outputs support neither `pattern` nor `minLength` nor
// numeric bounds, so the schema can ask for a grade but cannot require a *valid* one.
// This is the only thing standing between "the model said so" and a row in the catalogue,
// which is why it's a pure static rather than a branch inside the write path.
public class RouteImportAcceptanceTests
{
    private static TopoExtractionResult CleanRead(
        string name = "La Loca", string grade = "6b+", int? length = 25,
        string evidence = "La Loca 6b+ 25m", string confidence = "high") =>
        new(new ExtractedRoute(name, grade, length, evidence, confidence), false, null);

    [Fact]
    public void A_clean_high_confidence_read_is_accepted()
    {
        var (candidate, reason) = TopoImageReader.Accept(CleanRead());

        reason.ShouldBeNull();
        candidate.ShouldNotBeNull();
        candidate.Name.ShouldBe("La Loca");
        candidate.Grade.ShouldBe("6b+");
        candidate.Length.ShouldBe(25);
        candidate.Evidence.ShouldBe("La Loca 6b+ 25m");
    }

    // Confidence and evidence are the model's own admission of doubt, and this is where it
    // costs it something — the same bar SelectCandidates sets on an enrichment link.
    [Theory]
    [InlineData("medium")]
    [InlineData("low")]
    [InlineData("")]
    public void Anything_below_high_confidence_is_discarded(string confidence)
    {
        var (candidate, reason) = TopoImageReader.Accept(CleanRead(confidence: confidence));

        candidate.ShouldBeNull();
        reason.ShouldBe(RouteImportOutcomes.LowConfidence);
    }

    [Fact]
    public void A_high_confidence_read_with_no_evidence_quote_is_discarded()
    {
        var (candidate, reason) = TopoImageReader.Accept(CleanRead(evidence: "   "));

        candidate.ShouldBeNull();
        reason.ShouldBe(RouteImportOutcomes.LowConfidence);
    }

    [Fact]
    public void Multiple_routes_with_no_dominant_one_is_discarded()
    {
        var (candidate, reason) = TopoImageReader.Accept(
            new TopoExtractionResult(null, MultipleRoutes: true, "several lines drawn, none highlighted"));

        candidate.ShouldBeNull();
        reason.ShouldBe(RouteImportOutcomes.MultipleRoutes);
    }

    // multipleRoutes alongside a route is the "one is unmistakably the subject" case the
    // prompt carves out — a guidebook page devoted to one line on a busy wall. Accepted:
    // the discard is for the ambiguous case, not for every crowded topo.
    [Fact]
    public void Multiple_routes_with_one_dominant_is_accepted()
    {
        var (candidate, reason) = TopoImageReader.Accept(CleanRead() with { MultipleRoutes = true });

        reason.ShouldBeNull();
        candidate.ShouldNotBeNull();
    }

    [Fact]
    public void No_route_at_all_is_discarded_for_a_missing_name()
    {
        var (candidate, reason) = TopoImageReader.Accept(
            new TopoExtractionResult(null, false, "no route name printed on the panel"));

        candidate.ShouldBeNull();
        reason.ShouldBe(RouteImportOutcomes.NoRouteName);
    }

    [Fact]
    public void A_blank_name_is_discarded()
    {
        var (candidate, reason) = TopoImageReader.Accept(CleanRead(name: "   "));

        candidate.ShouldBeNull();
        reason.ShouldBe(RouteImportOutcomes.NoRouteName);
    }

    // The model is told to copy the grade and never convert it, so a UIAA or YDS grade
    // arrives verbatim and is rejected here — an unconvertible grade is a discard with a
    // reason, not a best effort, and certainly not a silent conversion.
    [Theory]
    [InlineData("V+")]
    [InlineData("5.11a")]
    [InlineData("E2 5b")]
    [InlineData("")]
    [InlineData("muy difícil")]
    public void An_unconvertible_grade_is_discarded(string grade)
    {
        var (candidate, reason) = TopoImageReader.Accept(CleanRead(grade: grade));

        candidate.ShouldBeNull();
        reason.ShouldBe(RouteImportOutcomes.UnreadableGrade);
    }

    // Grade is an exact-match filter key and a Distinct() list, so the stored form has to
    // be canonical — the accepted candidate carries the same normalisation the human-entry
    // path applies, not the raw string off the photo.
    [Fact]
    public void The_grade_is_normalised_the_way_the_write_path_stores_it()
    {
        TopoImageReader.Accept(CleanRead(grade: " 6B+ ")).Candidate!.Grade.ShouldBe("6b+");
    }

    [Fact]
    public void The_name_is_trimmed()
    {
        TopoImageReader.Accept(CleanRead(name: "  La Loca  ")).Candidate!.Name.ShouldBe("La Loca");
    }

    // ClimbingRoute.Length has no "unknown" value, so a topo that doesn't state one — or
    // states something the human-entry form would reject — leaves the column default
    // rather than writing a guess into a field a reviewer will read as fact.
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(3000)]
    [InlineData(-10)]
    public void An_absent_or_implausible_length_falls_back_to_the_column_default(int? length)
    {
        TopoImageReader.Accept(CleanRead(length: length)).Candidate!.Length.ShouldBe(0);
    }

    [Fact]
    public void A_plausible_length_is_kept()
    {
        TopoImageReader.Accept(CleanRead(length: 25)).Candidate!.Length.ShouldBe(25);
    }
}
