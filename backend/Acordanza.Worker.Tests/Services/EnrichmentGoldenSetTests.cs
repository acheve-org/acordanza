using System.Text.Json;
using System.Text.Json.Serialization;
using Acordanza.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// Plan 29 §7 — the acceptance gate for the whole plan, and the only test in this repo that
// makes real Claude and real network calls. It is neither unit- nor integration-tier: it is
// a measurement harness, run by hand before and after a prompt change, and BOTH CI test
// jobs exclude Category=Enrichment (.github/workflows/ci-cd.yml) so a push never spends
// money on live Foundry calls.
//
// Precision is the target, not recall: two correct links beat five of which three are
// wrong, and returning nothing on an obscure route is a success, not a miss. The two
// numbers it reports are precision (of what would have been appended, how much was right)
// and the "correctly returned nothing" rate.
//
// The set itself lives in GoldenSet.json and is checked in empty — see that file's comment.
[Trait("Category", "Enrichment")]
public class EnrichmentGoldenSetTests(ITestOutputHelper output)
{
    private sealed record GoldenRoute(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("grade")] string Grade,
        [property: JsonPropertyName("areaBreadcrumb")] string AreaBreadcrumb,
        [property: JsonPropertyName("firstAscensionists")] List<string>? FirstAscensionists,
        [property: JsonPropertyName("length")] int Length,
        [property: JsonPropertyName("orientation")] string? Orientation,
        [property: JsonPropertyName("gear")] string? Gear,
        [property: JsonPropertyName("lat")] double? Lat,
        [property: JsonPropertyName("lng")] double? Lng,
        [property: JsonPropertyName("expectedUrls")] List<string> ExpectedUrls);

    private sealed record GoldenSet([property: JsonPropertyName("routes")] List<GoldenRoute> Routes);

    [Fact]
    public async Task The_golden_set_reports_precision_and_the_correctly_empty_rate()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Services", "GoldenSet.json");
        var set = JsonSerializer.Deserialize<GoldenSet>(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        if (set is null || set.Routes.Count == 0)
        {
            Assert.Skip($"No golden-set entries in {path} — see that file's comment for how to fill it.");
            return;
        }

        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
        if (string.IsNullOrWhiteSpace(configuration["Anthropic:FoundryResource"]))
        {
            Assert.Skip("Anthropic__FoundryResource is not set — this harness makes real Foundry calls.");
            return;
        }

        var provider = new ClaudeRouteEnrichmentProvider(
            configuration, NullLogger<ClaudeRouteEnrichmentProvider>.Instance);

        var correct = 0;
        var appended = 0;
        var expectedNothing = 0;
        var correctlyNothing = 0;

        foreach (var route in set.Routes)
        {
            var context = new RouteEnrichmentContext(
                route.Name, route.Grade, route.AreaBreadcrumb, route.FirstAscensionists ?? [],
                route.Length, route.Orientation, route.Gear, route.Lat, route.Lng);

            var result = await provider.FindAsync(context, TestContext.Current.CancellationToken);

            // Exactly the selection production applies, minus the reachability request —
            // the point of measuring here is the model's judgment, not the network's.
            var selected = RouteEnrichmentService.ApplyCaps(
                RouteEnrichmentService.SelectCandidates(result, new HashSet<string>(), topoImported: false),
                existingLinkCount: 0);

            var expected = route.ExpectedUrls
                .Select(ClaudeRouteEnrichmentProvider.Canonical)
                .ToHashSet();
            var hits = selected.Count(l => expected.Contains(ClaudeRouteEnrichmentProvider.Canonical(l.Url)));

            appended += selected.Count;
            correct += hits;
            if (expected.Count == 0)
            {
                expectedNothing++;
                if (selected.Count == 0) correctlyNothing++;
            }

            output.WriteLine(
                $"{route.Name} ({route.AreaBreadcrumb}): completed={result.Completed} " +
                $"appended={selected.Count} expected={expected.Count} correct={hits}");
            foreach (var link in selected)
            {
                var verdict = expected.Contains(ClaudeRouteEnrichmentProvider.Canonical(link.Url)) ? "OK  " : "MISS";
                output.WriteLine($"    {verdict} [{link.Kind}] {link.Url}");
                output.WriteLine($"         evidence: {link.Evidence}");
            }
        }

        output.WriteLine("");
        output.WriteLine($"precision: {(appended == 0 ? "n/a (nothing appended)" : $"{correct}/{appended} = {(double)correct / appended:P0}")}");
        output.WriteLine($"correctly returned nothing: {correctlyNothing}/{expectedNothing}");

        // Deliberately no threshold assertion. This is a measurement, and a run that reports
        // a drop is exactly as useful as one that reports a rise — failing the build on a
        // number that costs $15 to reproduce would only push people to stop running it.
    }
}
