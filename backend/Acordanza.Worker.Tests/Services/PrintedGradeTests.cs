using Acordanza.Worker.Services;
using Shouldly;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// Unit-tier (no Docker, no network, no Foundry). Both extraction prompts tell the model to
// copy the grade exactly as printed, so this is the one thing standing between what a topo
// or a blog post prints in the grade line and ClimbingRoute.Grade — and the interesting
// half of it is everything it refuses to read, not what it reads.
//
// The case that produced it: «Peña Solano — Núria — MD+ 6c (6a+ obl) 200 mts», a topo a
// human reads at a glance, discarded as an unreadable grade.
public class PrintedGradeTests
{
    [Theory]
    [InlineData("6c")]
    [InlineData("6c+")]
    [InlineData(" 6C+ ")]
    [InlineData("9a")]
    [InlineData("1a")]
    public void A_clean_french_grade_passes_straight_through(string printed)
    {
        var parsed = PrintedGrade.Parse(printed);

        parsed.ShouldNotBeNull();
        parsed.Grade.ShouldBe(printed.Trim().ToLowerInvariant());
        parsed.ObligatoryGrade.ShouldBeNull();
    }

    // The reported case, and the shapes around it: the alpine difficulty grade is dropped
    // (this log has no field for it), the free grade is the route's, the parenthesised one
    // is the obligatory grade.
    [Theory]
    [InlineData("MD+ 6c (6a+ obl)", "6c", "6a+")]
    [InlineData("ED2 7a (6b oblig.)", "7a", "6b")]
    [InlineData("TD- 6a+(5c obl)", "6a+", "5c")]
    [InlineData("md 6b (6a obligado)", "6b", "6a")]
    [InlineData("MD+ 6c SR2+ (6a+ obl)", "6c", "6a+")]
    [InlineData("MD+/- 6c (6a+ obl., muy sostenido)", "6c", "6a+")]
    [InlineData("6c (6a+obl)", "6c", "6a+")]
    public void An_alpine_line_is_pulled_apart(string printed, string grade, string obligatory)
    {
        var parsed = PrintedGrade.Parse(printed);

        parsed.ShouldNotBeNull();
        parsed.Grade.ShouldBe(grade);
        parsed.ObligatoryGrade.ShouldBe(obligatory);
    }

    // The label need not be inside parentheses, and an unclosed one must not change the
    // reading — a topo set in a narrow column produces both.
    [Theory]
    [InlineData("6c 6a+ obl", "6c", "6a+")]
    [InlineData("6c (6a+ obl", "6c", "6a+")]
    [InlineData("MD+ 6c obl. 6a+", "6c", "6a+")]
    public void The_obligatory_grade_is_recognised_by_its_label_not_by_its_brackets(
        string printed, string grade, string obligatory)
    {
        var parsed = PrintedGrade.Parse(printed);

        parsed.ShouldNotBeNull();
        parsed.Grade.ShouldBe(grade);
        parsed.ObligatoryGrade.ShouldBe(obligatory);
    }

    // Decoration this log has nowhere to put, and nothing else on the line: still one
    // French grade, still readable.
    [Theory]
    [InlineData("MD+ 6c", "6c")]
    [InlineData("6c R", "6c")]
    [InlineData("6c (equipada)", "6c")]
    [InlineData("6c (6a+)", "6c")]
    public void Decoration_is_dropped_and_an_unlabelled_bracket_is_not_guessed_at(string printed, string grade)
    {
        var parsed = PrintedGrade.Parse(printed);

        parsed.ShouldNotBeNull();
        parsed.Grade.ShouldBe(grade);
        // «6c (6a+)» is the interesting one: the parenthesised grade *is* usually the
        // obligatory one, but "usually" is a guess, and a guessed obligatory grade reads
        // as fact on the route card. The grade still gets through.
        parsed.ObligatoryGrade.ShouldBeNull();
    }

    // The whole point of the strict half. A scale this log doesn't accept must stay a
    // discard: mining «E2 5b» for the substring that looks French would file a British
    // technical grade as a French sport grade, which is precisely the silent conversion
    // both prompts are written to prevent.
    [Theory]
    [InlineData("V+")]           // UIAA
    [InlineData("5.11a")]        // YDS
    [InlineData("E2 5b")]        // British trad: adjectival + technical
    [InlineData("HVS 5a")]
    [InlineData("VI+ (A1)")]
    [InlineData("muy difícil")]
    [InlineData("proyecto")]
    [InlineData("?")]
    [InlineData("")]
    [InlineData(null)]
    public void A_grade_from_another_scale_is_not_mined_for_a_french_looking_substring(string? printed) =>
        PrintedGrade.Parse(printed).ShouldBeNull();

    // Two different grades outside the brackets is a range or a pitch list, and this log
    // stores one grade. A stated failure the submitter can act on beats a coin flip —
    // the same call TopoImageReader.Accept makes on multipleRoutes. «6c/A1» used to be a
    // discard here too, before plan 34 gave '/' exactly one recognised meaning — see the
    // Split_grade tests below for what it resolves to now.
    [Theory]
    [InlineData("6a/6b")]
    [InlineData("6a a 6c")]
    [InlineData("L1 6a L2 6c")]
    [InlineData("(6a+ obl)")]
    public void An_ambiguous_or_absent_free_grade_is_not_resolved(string printed) =>
        PrintedGrade.Parse(printed).ShouldBeNull();

    // Plan 34 §11: '/' gets exactly one recognised meaning — one French grade and one aid
    // grade, in either order. «6c/A1» is a route with both, not a range.
    [Theory]
    [InlineData("6c/A1", "6c", "A1")]
    [InlineData("A1/6c", "6c", "A1")]
    [InlineData("6c/a1", "6c", "A1")]
    public void A_combined_free_and_aid_token_splits_into_both_grades(string printed, string grade, string aidGrade)
    {
        var parsed = PrintedGrade.Parse(printed);

        parsed.ShouldNotBeNull();
        parsed.Grade.ShouldBe(grade);
        parsed.AidGrade.ShouldBe(aidGrade);
        parsed.ObligatoryGrade.ShouldBeNull();
    }

    // An aid grade alone — no free grade at all.
    [Theory]
    [InlineData("A2")]
    [InlineData(" a2 ")]
    [InlineData("C1")]
    [InlineData("A3+")]
    public void An_aid_only_token_yields_no_free_grade(string printed)
    {
        var parsed = PrintedGrade.Parse(printed);

        parsed.ShouldNotBeNull();
        parsed.Grade.ShouldBeNull();
        parsed.AidGrade.ShouldBe(printed.Trim().ToUpperInvariant());
    }

    // Two French grades stay a range even when one side reads like it could combine —
    // «6a/6b» states no single grade on either scale, and this log stores one.
    [Fact]
    public void Two_french_grades_over_a_slash_still_resolve_to_nothing()
    {
        PrintedGrade.Parse("6a/6b").ShouldBeNull();
    }

    // The multi-pitch case with all three facts present at once: alpine difficulty
    // dropped, free grade, aid grade and obligatory grade all recovered.
    [Fact]
    public void A_multi_pitch_line_with_a_combined_grade_yields_all_three_facts()
    {
        var parsed = PrintedGrade.Parse("MD+ 6c/A1 (6a+ obl)");

        parsed.ShouldNotBeNull();
        parsed.Grade.ShouldBe("6c");
        parsed.AidGrade.ShouldBe("A1");
        parsed.ObligatoryGrade.ShouldBe("6a+");
    }

    // Repetition isn't ambiguity.
    [Fact]
    public void The_same_grade_stated_twice_is_still_one_grade()
    {
        var parsed = PrintedGrade.Parse("MD+ 6c, 6c");

        parsed.ShouldNotBeNull();
        parsed.Grade.ShouldBe("6c");
    }

    // Grade is an exact-match filter key and a Distinct() list, so what comes out has to
    // be canonical whether it arrived clean or buried.
    [Fact]
    public void The_extracted_grade_is_normalised_the_way_the_write_path_stores_it()
    {
        var parsed = PrintedGrade.Parse(" MD+ 6C (6A+ OBL) ");

        parsed.ShouldNotBeNull();
        parsed.Grade.ShouldBe("6c");
        parsed.ObligatoryGrade.ShouldBe("6a+");
    }
}
