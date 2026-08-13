using System.Text.Json;
using Acordanza.Worker.Services;
using Anthropic.Models.Messages;
using Shouldly;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// Plan 29 §1 — unit-tier (no Docker, no network, no Claude): the deterministic half of the
// enrichment quality fix. A link the model didn't actually retrieve never reaches the
// route, and the URL comparison that decides that has to be forgiving about the ways a URL
// gets decorated between a search result and a model's answer without being forgiving about
// which page it points at.
public class EnrichmentGroundingTests
{
    [Theory]
    // Tracking parameters carry no identity — a link differing from its search result only
    // by ?utm_source= must not be dropped as ungrounded.
    [InlineData("https://example.com/via?utm_source=x&utm_medium=y", "https://example.com/via")]
    [InlineData("https://example.com/via?fbclid=abc", "https://example.com/via")]
    [InlineData("https://example.com/via?gclid=abc&id=7", "https://example.com/via?id=7")]
    // Fragment, trailing slash, host case.
    [InlineData("https://example.com/via#topo", "https://example.com/via")]
    [InlineData("https://example.com/via/", "https://example.com/via")]
    [InlineData("https://EXAMPLE.com/via", "https://example.com/via")]
    [InlineData("HTTPS://example.com/via", "https://example.com/via")]
    [InlineData("https://example.com/", "https://example.com")]
    public void Canonical_normalises_away_everything_that_isnt_identity(string url, string equivalent)
    {
        ClaudeRouteEnrichmentProvider.Canonical(url)
            .ShouldBe(ClaudeRouteEnrichmentProvider.Canonical(equivalent));
    }

    [Theory]
    // Path case, a different page, a different host, and a non-tracking query parameter all
    // stay distinct — over-normalising would ground a link against the wrong page.
    [InlineData("https://example.com/via", "https://example.com/otra")]
    [InlineData("https://example.com/via", "https://otro.com/via")]
    [InlineData("https://example.com/via", "https://example.com/via?page=2")]
    [InlineData("https://example.com/via", "http://example.com/via")]
    [InlineData("https://example.com/Via", "https://example.com/via")]
    public void Canonical_keeps_genuinely_different_urls_apart(string url, string other)
    {
        ClaudeRouteEnrichmentProvider.Canonical(url)
            .ShouldNotBe(ClaudeRouteEnrichmentProvider.Canonical(other));
    }

    [Fact]
    public void A_link_absent_from_the_retrieved_set_is_dropped_and_a_present_one_survives()
    {
        var retrieved = new HashSet<string>
        {
            ClaudeRouteEnrichmentProvider.Canonical("https://example.com/real")
        };
        var returned = new List<EnrichmentLink>
        {
            Link("https://example.com/real?utm_source=chat"), // same page, decorated
            Link("https://example.com/invented")              // never retrieved
        };

        var grounded = ClaudeRouteEnrichmentProvider.Ground(returned, retrieved);

        grounded.Select(l => l.Url).ShouldBe(["https://example.com/real?utm_source=chat"]);
    }

    [Fact]
    public void An_empty_retrieved_set_drops_everything()
    {
        var returned = new List<EnrichmentLink> { Link("https://example.com/a"), Link("https://example.com/b") };

        ClaudeRouteEnrichmentProvider.Ground(returned, []).ShouldBeEmpty();
    }

    [Fact]
    public void Retrieved_urls_are_collected_from_both_search_results_and_fetched_pages()
    {
        // Built from the wire shape rather than from object initialisers: these SDK blocks
        // carry a dozen `required` members apiece, and a hand-built one would pin our
        // guesses about them instead of what the API actually sends.
        var content = JsonSerializer.Deserialize<List<ContentBlock>>("""
            [
              { "type": "text", "text": "{}", "citations": null },
              {
                "type": "web_search_tool_result",
                "tool_use_id": "srvtoolu_1",
                "content": [
                  {
                    "type": "web_search_result",
                    "url": "https://example.com/resultado/",
                    "title": "Resultado",
                    "encrypted_content": "x",
                    "page_age": null
                  }
                ]
              },
              {
                "type": "web_fetch_tool_result",
                "tool_use_id": "srvtoolu_2",
                "content": {
                  "type": "web_fetch_result",
                  "url": "https://example.com/abierta#seccion",
                  "retrieved_at": "2026-08-13T00:00:00Z",
                  "content": {
                    "type": "document",
                    "title": "Abierta",
                    "citations": null,
                    "source": { "type": "text", "media_type": "text/plain", "data": "…" }
                  }
                }
              }
            ]
            """)!;

        var retrieved = ClaudeRouteEnrichmentProvider.RetrievedUrls(content);

        retrieved.ShouldBe(
            [
                ClaudeRouteEnrichmentProvider.Canonical("https://example.com/resultado"),
                ClaudeRouteEnrichmentProvider.Canonical("https://example.com/abierta")
            ],
            ignoreOrder: true);
    }

    [Theory]
    // The three ways a run ends without a trustworthy payload: a server-side tool loop that
    // hit its iteration cap, a budget that truncated the JSON mid-object, and Opus 5's
    // safety classifiers answering on an HTTP 200. All three used to be unchecked — the
    // first parsed to an empty result and still marked the route enriched, the second threw
    // out of Deserialize into five identical queue redeliveries.
    [InlineData("pause_turn")]
    [InlineData("max_tokens")]
    [InlineData("refusal")]
    [InlineData("tool_use")]
    public void A_stop_reason_that_isnt_end_turn_marks_the_run_incomplete(string stopReason) =>
        ClaudeRouteEnrichmentProvider.IsIncomplete(stopReason).ShouldBeTrue();

    [Theory]
    [InlineData("end_turn")]
    [InlineData(null)]
    public void End_turn_is_the_only_completed_run(string? stopReason) =>
        ClaudeRouteEnrichmentProvider.IsIncomplete(stopReason).ShouldBeFalse();

    private static EnrichmentLink Link(string url) =>
        new(url, "Título", "first-ascent", "cita de la página", "high");
}
