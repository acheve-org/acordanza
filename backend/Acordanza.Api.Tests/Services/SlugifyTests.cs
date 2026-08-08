using Acordanza.Api.Services;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Services;

public class SlugifyTests
{
    [Theory]
    [InlineData("Peña Rúa", "pena-rua")]
    [InlineData("Área  --  Norte!!", "area-norte")]
    [InlineData("  -leading and trailing-  ", "leading-and-trailing")]
    [InlineData("Já Ñandú Über", "ja-nandu-uber")]
    public void ToSlug_normalizes_accents_punctuation_and_edges(string input, string expected)
    {
        Slugify.ToSlug(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("---")]
    public void ToSlug_falls_back_to_sector_when_result_would_be_empty(string input)
    {
        Slugify.ToSlug(input).ShouldBe("sector");
    }

    [Fact]
    public void ToSlug_honours_a_custom_fallback()
    {
        Slugify.ToSlug("!!!", "aperturista").ShouldBe("aperturista");
    }
}
