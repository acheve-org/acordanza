using Acordanza.Api.Models;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Models;

// Issue #152: the French sport-grade scale is the only one the app accepts. This table
// is duplicated verbatim by design-system's GradePill.test.tsx (gradeBand) — the
// duplication is deliberate, a drift detector between the two grammars.
public class GradeValidatorTests
{
    [Theory]
    [InlineData("1a")]
    [InlineData("5c")]
    [InlineData("6a")]
    [InlineData("6a+")]
    [InlineData("7c+")]
    [InlineData("8b")]
    [InlineData("9c+")]
    [InlineData("6A")]
    [InlineData(" 6a ")]
    [InlineData("7C+")]
    public void IsValid_accepts_the_french_scale(string grade)
    {
        GradeValidator.IsValid(grade).ShouldBeTrue();
    }

    [Theory]
    [InlineData("0a")]
    [InlineData("10a")]
    [InlineData("6d")]
    [InlineData("6")]
    [InlineData("a")]
    [InlineData("+")]
    [InlineData("6a++")]
    [InlineData("6 a")]
    [InlineData("6a+extra")]
    [InlineData("V+")]
    [InlineData("E2 5b")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValid_rejects_everything_else(string? grade)
    {
        GradeValidator.IsValid(grade).ShouldBeFalse();
    }

    [Fact]
    public void Normalize_trims_and_lowercases()
    {
        GradeValidator.Normalize("  7C+ ").ShouldBe("7c+");
    }

    [Fact]
    public void Normalize_turns_null_into_empty_string()
    {
        GradeValidator.Normalize(null).ShouldBe("");
    }
}
