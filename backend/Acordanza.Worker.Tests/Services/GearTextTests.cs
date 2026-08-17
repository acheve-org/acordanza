using Acordanza.Worker.Services;
using Shouldly;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// Plan 33 §3 — the one defensive clamp between a model's Gear answer and
// ClimbingRoute.RecommendedGear, mirroring how PrintedGrade/AcceptableLength are pinned by
// their own unit tests.
public class GearTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_or_whitespace_becomes_null(string? gear)
    {
        GearText.Truncate(gear).ShouldBeNull();
    }

    [Fact]
    public void A_normal_short_string_passes_through_trimmed()
    {
        GearText.Truncate("  12 exprés y cuerda de 70 m  ").ShouldBe("12 exprés y cuerda de 70 m");
    }

    [Fact]
    public void A_string_over_the_cap_is_truncated_to_exactly_the_cap()
    {
        var gear = new string('a', 600);

        var truncated = GearText.Truncate(gear, maxLength: 500);

        truncated!.Length.ShouldBe(500);
        truncated.ShouldBe(new string('a', 500));
    }
}
