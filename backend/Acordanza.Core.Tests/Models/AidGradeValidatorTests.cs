using Acordanza.Core.Models;
using Shouldly;
using Xunit;

namespace Acordanza.Core.Tests.Models;

// Plan 34: the artificial (aid) grade scale, A0–A5 for aid that places gear which can
// damage the rock, C0–C5 for clean aid, each optionally '+'. Sibling of GradeValidator's
// French-scale grammar, independent of it — see AidGradeValidator's doc comment.
public class AidGradeValidatorTests
{
    [Theory]
    [InlineData("A0")]
    [InlineData("A1")]
    [InlineData("A2")]
    [InlineData("A3")]
    [InlineData("A4")]
    [InlineData("A5")]
    [InlineData("C0")]
    [InlineData("C1")]
    [InlineData("C2")]
    [InlineData("C3")]
    [InlineData("C4")]
    [InlineData("C5")]
    [InlineData("A3+")]
    [InlineData("C2+")]
    public void IsValid_accepts_the_aid_scale(string grade)
    {
        AidGradeValidator.IsValid(grade).ShouldBeTrue();
    }

    // Normalize uppercases, unlike GradeValidator's lowercase — so lowercase input must
    // still validate.
    [Theory]
    [InlineData("a1")]
    [InlineData("c2+")]
    [InlineData(" a1 ")]
    public void IsValid_case_folds(string grade)
    {
        AidGradeValidator.IsValid(grade).ShouldBeTrue();
    }

    [Theory]
    [InlineData("A6")]
    [InlineData("C6")]
    [InlineData("B2")]
    [InlineData("A")]
    [InlineData("6a")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValid_rejects_everything_else(string? grade)
    {
        AidGradeValidator.IsValid(grade).ShouldBeFalse();
    }

    [Fact]
    public void Normalize_trims_and_uppercases()
    {
        AidGradeValidator.Normalize(" a1 ").ShouldBe("A1");
    }

    [Fact]
    public void Normalize_turns_null_into_empty_string()
    {
        AidGradeValidator.Normalize(null).ShouldBe("");
    }
}
