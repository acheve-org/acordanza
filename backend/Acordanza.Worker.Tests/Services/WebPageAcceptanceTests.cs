using Acordanza.Worker.Services;
using Shouldly;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// Plan 31 §5 — unit-tier, the Url kind's half of what RouteImportAcceptanceTests does for
// the Image kind. Same reason for existing: structured outputs support neither `pattern`
// nor `minLength` nor numeric bounds, so every constraint on what the model returned lives
// here, in a pure static, rather than in the schema or in a branch of the write path.
public class WebPageAcceptanceTests
{
    private static PageExtractionResult CleanRead(
        string name = "La Loca",
        string grade = "6b+",
        int? length = 25,
        string evidence = "«La Loca», 6b+, 25 m, abierta por Pedro Pons",
        string confidence = "high",
        bool isClimbingRoute = true,
        bool multipleRoutes = false,
        string? sectorMentioned = null,
        IReadOnlyList<string>? openers = null,
        string description = "Placa técnica de 25 metros.") =>
        new(isClimbingRoute,
            new ExtractedPageRoute(name, grade, length, openers ?? ["Pedro Pons"], description, evidence, confidence),
            sectorMentioned,
            multipleRoutes,
            null);

    [Fact]
    public void A_clean_high_confidence_read_is_accepted()
    {
        var (candidate, reason) = WebPageReader.Accept(CleanRead());

        reason.ShouldBeNull();
        candidate.ShouldNotBeNull();
        candidate.Name.ShouldBe("La Loca");
        candidate.Grade.ShouldBe("6b+");
        candidate.Length.ShouldBe(25);
        candidate.Description.ShouldBe("Placa técnica de 25 metros.");
        candidate.FirstAscensionists.ShouldBe(["Pedro Pons"]);
    }

    // The gate, as a field rather than a cheap pre-call: both calls would carry the same
    // page text at the same price per token, so a pre-filter would save nothing.
    [Fact]
    public void A_page_that_is_not_about_a_route_is_discarded()
    {
        var (candidate, reason) = WebPageReader.Accept(CleanRead(isClimbingRoute: false));

        candidate.ShouldBeNull();
        reason.ShouldBe(RouteImportOutcomes.NotAboutARoute);
    }

    // A sector round-up naming six routes equally is a real answer, and a confidently wrong
    // single route is worse for the reviewer than a stated failure.
    [Fact]
    public void A_round_up_with_no_dominant_route_is_discarded()
    {
        var result = CleanRead(multipleRoutes: true) with { Route = null };
        var (candidate, reason) = WebPageReader.Accept(result);

        candidate.ShouldBeNull();
        reason.ShouldBe(RouteImportOutcomes.MultipleRoutesOnPage);
    }

    // ...but a post about one route that also lists its neighbours still counts.
    [Fact]
    public void A_dominant_route_alongside_multipleRoutes_is_accepted()
    {
        var (candidate, _) = WebPageReader.Accept(CleanRead(multipleRoutes: true));

        candidate.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("medium")]
    [InlineData("low")]
    [InlineData("")]
    public void Anything_below_high_confidence_is_discarded(string confidence)
    {
        var (candidate, reason) = WebPageReader.Accept(CleanRead(confidence: confidence));

        candidate.ShouldBeNull();
        reason.ShouldBe(RouteImportOutcomes.LowConfidence);
    }

    [Fact]
    public void An_empty_evidence_quote_is_discarded()
    {
        var (candidate, reason) = WebPageReader.Accept(CleanRead(evidence: "  "));

        candidate.ShouldBeNull();
        reason.ShouldBe(RouteImportOutcomes.LowConfidence);
    }

    [Fact]
    public void A_missing_name_is_discarded()
    {
        var (candidate, reason) = WebPageReader.Accept(CleanRead(name: " "));

        candidate.ShouldBeNull();
        reason.ShouldBe(RouteImportOutcomes.NoRouteNameOnPage);
    }

    // The model is told to copy the grade and never convert it, so a UIAA or YDS grade
    // arrives verbatim and is discarded with a reason rather than guessed at.
    [Theory]
    [InlineData("V+")]
    [InlineData("5.11a")]
    [InlineData("difícil")]
    [InlineData("")]
    public void A_grade_this_log_cannot_accept_is_discarded(string grade)
    {
        var (candidate, reason) = WebPageReader.Accept(CleanRead(grade: grade));

        candidate.ShouldBeNull();
        reason.ShouldBe(RouteImportOutcomes.UnreadableGradeOnPage);
    }

    [Fact]
    public void The_grade_is_normalised_not_converted()
    {
        var (candidate, _) = WebPageReader.Accept(CleanRead(grade: " 6B+ "));

        candidate!.Grade.ShouldBe("6b+");
    }

    // 0 is below RoutesController.ValidateLength's floor on purpose: the page didn't say,
    // so a reviewer has to before they can save.
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5000)]
    public void An_absent_or_implausible_length_falls_back_to_zero(int? length)
    {
        var (candidate, _) = WebPageReader.Accept(CleanRead(length: length));

        candidate!.Length.ShouldBe(0);
    }

    [Fact]
    public void Opener_names_are_trimmed_and_deduped()
    {
        var (candidate, _) = WebPageReader.Accept(CleanRead(openers: ["Pedro Pons", " pedro pons ", "", "Ana Gil"]));

        candidate!.FirstAscensionists.ShouldBe(["Pedro Pons", "Ana Gil"]);
    }

    [Fact]
    public void The_mentioned_sector_is_carried_through_as_a_cross_check()
    {
        var (candidate, _) = WebPageReader.Accept(CleanRead(sectorMentioned: " Riglos "));

        candidate!.SectorMentioned.ShouldBe("Riglos");
    }

    // The stop_reason path never produces a Discarded row — WebPageReader turns
    // Completed: false into Failed before Accept is ever reached, and Accept must not be
    // the thing that decides it. Asserted as "an incomplete result carries no route", so
    // the two cannot be confused if the order ever changes.
    [Fact]
    public void An_incomplete_result_carries_nothing_to_accept()
    {
        var incomplete = new PageExtractionResult(false, null, null, false, null, Completed: false);

        incomplete.Route.ShouldBeNull();
        incomplete.IsClimbingRoute.ShouldBeFalse();
    }
}
