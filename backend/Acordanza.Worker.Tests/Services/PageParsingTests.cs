using Acordanza.Worker.Services;
using Shouldly;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// Plan 31 §4's two pure extractors. Unit-tier, no Docker and no network — which is most of
// the argument for regex over a parser dependency: the whole of the HTML handling can be
// pinned by tests that run in milliseconds.
public class PageTextTests
{
    [Fact]
    public void Script_style_and_noscript_blocks_are_dropped_with_their_contents()
    {
        var text = PageText.Extract("""
            <html><head><style>.a { color: red }</style>
            <script>var route = "no soy texto";</script></head>
            <body><noscript>activa JavaScript</noscript><p>La Loca, 6b+</p></body></html>
            """);

        text.ShouldBe("La Loca, 6b+");
    }

    [Fact]
    public void Comments_are_dropped()
    {
        PageText.Extract("<p>visible<!-- oculto --></p>").ShouldBe("visible");
    }

    [Fact]
    public void Entities_are_decoded_and_whitespace_collapsed()
    {
        PageText.Extract("<p>Pe&ntilde;a\n\n   Rubia &amp;   Riglos</p>").ShouldBe("Peña Rubia & Riglos");
    }

    // A tag is a boundary: dropping it outright would run "6b+" into "25 m".
    [Fact]
    public void Adjacent_tags_do_not_run_their_text_together()
    {
        PageText.Extract("<td>6b+</td><td>25 m</td>").ShouldBe("6b+ 25 m");
    }

    [Fact]
    public void Text_is_capped()
    {
        var html = "<p>" + new string('a', PageText.MaxCharacters * 2) + "</p>";
        PageText.Extract(html).Length.ShouldBe(PageText.MaxCharacters);
    }

    // The JS-rendered/paywalled case: WebPageReader turns anything under this into a stated
    // discard rather than sending the model an empty page to hallucinate into.
    [Fact]
    public void A_page_with_no_prose_strips_to_almost_nothing()
    {
        var html = "<html><body><div id=\"root\"></div><script>" + new string('x', 5000) + "</script></body></html>";
        PageText.Extract(html).Length.ShouldBeLessThan(PageText.MinimumUsefulCharacters);
    }
}

public class PageImagesTests
{
    private static readonly Uri PageUri = new("https://blog.example.com/posts/la-loca");

    private static List<string> Extract(string html, string? baseUrl = null) =>
        PageImages.Extract(html, baseUrl is null ? PageUri : new Uri(baseUrl))
            .Select(u => u.ToString())
            .ToList();

    // On a post about an opening the lead image is the topo more often than not, and the
    // sweep stops at the first hit — so this ordering is what keeps the typical run at one
    // vision call.
    [Fact]
    public void Og_image_comes_before_document_order()
    {
        var urls = Extract("""
            <meta property="og:image" content="https://cdn.example.com/lead.jpg">
            <img src="https://cdn.example.com/first-in-body.jpg">
            """);

        urls.ShouldBe(["https://cdn.example.com/lead.jpg", "https://cdn.example.com/first-in-body.jpg"]);
    }

    [Fact]
    public void Twitter_image_is_read_too()
    {
        Extract("""<meta name="twitter:image" content="https://cdn.example.com/card.jpg">""")
            .ShouldContain("https://cdn.example.com/card.jpg");
    }

    [Fact]
    public void The_largest_srcset_entry_wins()
    {
        var urls = Extract("""
            <img srcset="https://cdn.example.com/small.jpg 320w, https://cdn.example.com/big.jpg 1600w">
            """);

        urls.ShouldContain("https://cdn.example.com/big.jpg");
        urls.ShouldNotContain("https://cdn.example.com/small.jpg");
    }

    // blogspot and wordpress — exactly the long-tail hosts plan 29 §3 kept in scope — put
    // a placeholder in src and the real image in a data- attribute.
    [Theory]
    [InlineData("data-src")]
    [InlineData("data-lazy-src")]
    [InlineData("data-original")]
    public void Lazy_load_attributes_are_read(string attribute)
    {
        Extract($"""<img {attribute}="https://cdn.example.com/topo.jpg">""")
            .ShouldContain("https://cdn.example.com/topo.jpg");
    }

    [Fact]
    public void Relative_urls_resolve_against_the_final_response_uri()
    {
        Extract("""<img src="/media/topo.jpg">""")
            .ShouldContain("https://blog.example.com/media/topo.jpg");
    }

    [Fact]
    public void A_declared_base_href_wins_over_the_response_uri()
    {
        Extract("""
            <base href="https://cdn.example.com/assets/">
            <img src="topo.jpg">
            """)
            .ShouldContain("https://cdn.example.com/assets/topo.jpg");
    }

    [Fact]
    public void Data_uris_are_dropped()
    {
        Extract("""<img src="data:image/png;base64,iVBORw0KGgo=">""").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("https://cdn.example.com/avatar/me.png")]
    [InlineData("https://cdn.example.com/gravatar.jpg")]
    [InlineData("https://cdn.example.com/icons/share.png")]
    [InlineData("https://cdn.example.com/site-logo.svg")]
    [InlineData("https://cdn.example.com/emoji/smile.png")]
    [InlineData("https://cdn.example.com/badge.png")]
    [InlineData("https://cdn.example.com/sprite.png")]
    [InlineData("https://cdn.example.com/spinner.gif")]
    public void Obvious_furniture_is_filtered_on_path(string url)
    {
        Extract($"""<img src="{url}">""").ShouldBeEmpty();
    }

    [Fact]
    public void Images_declaring_a_dimension_under_100px_are_dropped()
    {
        Extract("""<img src="https://cdn.example.com/pixel.gif" width="1" height="1">""").ShouldBeEmpty();
        Extract("""<img src="https://cdn.example.com/topo.jpg" width="800" height="600">""")
            .ShouldContain("https://cdn.example.com/topo.jpg");
    }

    // Same image, one with a tracking parameter: one vision call, not two.
    [Fact]
    public void Candidates_are_deduped_through_the_canonicaliser()
    {
        Extract("""
            <img src="https://cdn.example.com/topo.jpg">
            <img src="https://cdn.example.com/topo.jpg?utm_source=rss">
            """)
            .Count.ShouldBe(1);
    }

    [Fact]
    public void The_list_is_capped()
    {
        var html = string.Join("", Enumerable.Range(0, 30)
            .Select(i => $"""<img src="https://cdn.example.com/photo-{i}.jpg">"""));

        Extract(html).Count.ShouldBe(PageImages.MaxCandidates);
    }

    [Fact]
    public void Non_http_schemes_are_dropped()
    {
        Extract("""<img src="ftp://example.com/topo.jpg">""").ShouldBeEmpty();
    }
}
