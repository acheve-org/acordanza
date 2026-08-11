using Acordanza.Api.Services;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Services;

// Moved from RoutesController.ResolveLinks when LinkRules was hoisted (plan 18) so the
// worker path can reuse it — same assertions, new home. RouteFieldsTests still covers
// the HTTP round trip end-to-end.
public class LinkRulesTests
{
    [Fact]
    public void Trims_and_drops_blank_entries()
    {
        var (urls, error) = LinkRules.Resolve(["  https://example.com/a  ", "   ", ""]);

        error.ShouldBeNull();
        urls.ShouldBe(["https://example.com/a"]);
    }

    [Fact]
    public void Dedupes_after_trimming()
    {
        var (urls, error) = LinkRules.Resolve(["https://example.com/a", "https://example.com/a"]);

        error.ShouldBeNull();
        urls.ShouldBe(["https://example.com/a"]);
    }

    [Fact]
    public void More_than_ten_links_is_rejected()
    {
        var links = Enumerable.Range(1, 11).Select(i => $"https://example.com/{i}").ToList();

        var (urls, error) = LinkRules.Resolve(links);

        urls.ShouldBeNull();
        error.ShouldNotBeNull();
    }

    [Fact]
    public void An_overlong_link_is_rejected()
    {
        var link = "https://example.com/" + new string('a', LinkRules.MaxLinkLength);

        var (urls, error) = LinkRules.Resolve([link]);

        urls.ShouldBeNull();
        error.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("ftp://x/y")]
    [InlineData("not a url")]
    public void A_disallowed_scheme_or_shape_is_rejected(string link)
    {
        var (urls, error) = LinkRules.Resolve([link]);

        urls.ShouldBeNull();
        error.ShouldNotBeNull();
    }

    [Fact]
    public void Http_and_https_are_both_allowed()
    {
        var (urls, error) = LinkRules.Resolve(["http://example.com/a", "https://example.com/b"]);

        error.ShouldBeNull();
        urls.ShouldBe(["http://example.com/a", "https://example.com/b"]);
    }

    [Fact]
    public void Null_input_resolves_to_an_empty_list()
    {
        var (urls, error) = LinkRules.Resolve(null);

        error.ShouldBeNull();
        urls.ShouldBeEmpty();
    }
}
