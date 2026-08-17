using System.Text.Json;
using System.Text.Json.Serialization;
using Acordanza.Core.Services;
using Acordanza.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// Plan 30 §5 — the acceptance gate for prompt changes to the route-import pipeline, and the
// second of the two harnesses in this repo that make real Foundry calls. Same tier as
// EnrichmentGoldenSetTests: neither unit nor integration, run by hand, and excluded from
// both CI test jobs by Category=Enrichment so a push never spends money.
//
// This is also the honest answer to "does it learn over time?". Claude models are not
// fine-tunable, so there is no training loop — but TopoImport is a labelled corpus
// accumulating for free, and the loop is a human periodically promoting rows into the
// few-shot prefixes and into RouteImportGoldenSet.json, then measuring here whether that
// helped. Real learning, measurable and reversible, with a person in the loop.
//
// Precision is the target, as in plan 29: a correct discard is a success, and the two
// numbers reported are the gate's precision/recall and the extraction's accuracy.
[Trait("Category", "Enrichment")]
public class RouteImportGoldenSetTests(ITestOutputHelper output)
{
    private sealed record GoldenImage(
        [property: JsonPropertyName("blobName")] string BlobName,
        [property: JsonPropertyName("isTopo")] bool IsTopo,
        [property: JsonPropertyName("areaBreadcrumb")] string AreaBreadcrumb,
        [property: JsonPropertyName("expectedName")] string? ExpectedName,
        [property: JsonPropertyName("expectedGrade")] string? ExpectedGrade,
        [property: JsonPropertyName("expectedGear")] string? ExpectedGear);

    private sealed record GoldenSet([property: JsonPropertyName("images")] List<GoldenImage> Images);

    [Fact]
    public async Task The_golden_set_reports_gate_precision_and_extraction_accuracy()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Services", "RouteImportGoldenSet.json");
        var set = JsonSerializer.Deserialize<GoldenSet>(
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        if (set is null || set.Images.Count == 0)
        {
            Assert.Skip($"No golden-set entries in {path} — see that file's comment for how to fill it.");
            return;
        }

        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        if (string.IsNullOrWhiteSpace(configuration["Anthropic:FoundryResource"]))
        {
            Assert.Skip("Anthropic__FoundryResource is not set — this harness makes real Foundry calls.");
            return;
        }

        var blobStorage = new BlobStorageService(configuration);
        var fewShot = new TopoImportFewShotSet(blobStorage, configuration, NullLogger<TopoImportFewShotSet>.Instance);
        var chatClient = FoundryClient.CreateChatClient(configuration);
        var gate = new ClaudeTopoImportGateProvider(chatClient, fewShot, NullLogger<ClaudeTopoImportGateProvider>.Instance);
        var extraction = new ClaudeTopoExtractionProvider(chatClient, NullLogger<ClaudeTopoExtractionProvider>.Instance);

        // Gate confusion matrix: "positive" is "this is a topo".
        var truePositives = 0;
        var falsePositives = 0;
        var falseNegatives = 0;

        // Extraction, over the entries the gate let through and that are really topos.
        var extractionAttempted = 0;
        var extractionCorrect = 0;

        // Gear's own match rate (plan 33) — reported alongside, not folded into
        // extractionCorrect, since gear carries no confidence gate of its own and a miss
        // here shouldn't read as a name/grade regression.
        var gearAttempted = 0;
        var gearCorrect = 0;

        foreach (var image in set.Images)
        {
            var blob = await blobStorage.DownloadAsync(image.BlobName);
            if (blob is null)
            {
                output.WriteLine($"{image.BlobName}: MISSING BLOB — skipped");
                continue;
            }

            var (bytes, contentType) = blob.Value;
            var gateResult = await gate.IsTopoAsync(
                new TopoImportGateContext(bytes, contentType), TestContext.Current.CancellationToken);

            if (gateResult.IsTopo && image.IsTopo) truePositives++;
            if (gateResult.IsTopo && !image.IsTopo) falsePositives++;
            if (!gateResult.IsTopo && image.IsTopo) falseNegatives++;

            output.WriteLine(
                $"{image.BlobName}: gate={gateResult.IsTopo} expected={image.IsTopo} " +
                $"completed={gateResult.Completed} reason={gateResult.Reason}");

            if (!gateResult.IsTopo || !image.IsTopo) continue;

            var extracted = await extraction.ExtractAsync(
                new TopoExtractionContext(bytes, contentType, image.AreaBreadcrumb),
                TestContext.Current.CancellationToken);

            // Exactly the predicate production applies, so this measures the shipped
            // behaviour rather than a second implementation of it.
            var (candidate, discardReason) = TopoImageReader.Accept(extracted);

            extractionAttempted++;
            // A correct discard counts as a hit: an entry whose expectedName is null is one
            // where discarding *is* the right answer.
            var correct = image.ExpectedName is null
                ? candidate is null
                : candidate is not null &&
                  string.Equals(candidate.Name, image.ExpectedName, StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(candidate.Grade, GradeValidatorNormalize(image.ExpectedGrade), StringComparison.OrdinalIgnoreCase);
            if (correct) extractionCorrect++;

            if (candidate is not null)
            {
                gearAttempted++;
                if (string.Equals(candidate.Gear, image.ExpectedGear, StringComparison.OrdinalIgnoreCase)) gearCorrect++;
            }

            output.WriteLine(
                $"    {(correct ? "OK  " : "MISS")} extracted=" +
                (candidate is null
                    ? $"(discarded: {discardReason})"
                    : $"'{candidate.Name}' {candidate.Grade} {candidate.Length}m") +
                $" expected=" + (image.ExpectedName is null ? "(discard)" : $"'{image.ExpectedName}' {image.ExpectedGrade}"));
            if (candidate is not null)
            {
                output.WriteLine($"         evidence: {candidate.Evidence}");
                output.WriteLine($"         gear: '{candidate.Gear}' expected='{image.ExpectedGear}'");
            }
        }

        var gatePredictedPositive = truePositives + falsePositives;
        var gateActualPositive = truePositives + falseNegatives;

        output.WriteLine("");
        output.WriteLine($"gate precision: {(gatePredictedPositive == 0 ? "n/a" : $"{truePositives}/{gatePredictedPositive} = {(double)truePositives / gatePredictedPositive:P0}")}");
        output.WriteLine($"gate recall:    {(gateActualPositive == 0 ? "n/a" : $"{truePositives}/{gateActualPositive} = {(double)truePositives / gateActualPositive:P0}")}");
        output.WriteLine($"extraction accuracy: {(extractionAttempted == 0 ? "n/a" : $"{extractionCorrect}/{extractionAttempted} = {(double)extractionCorrect / extractionAttempted:P0}")}");
        output.WriteLine($"gear match rate: {(gearAttempted == 0 ? "n/a" : $"{gearCorrect}/{gearAttempted} = {(double)gearCorrect / gearAttempted:P0}")}");

        // No threshold assertion, for the same reason EnrichmentGoldenSetTests has none:
        // this is a measurement, a run reporting a drop is as useful as one reporting a
        // rise, and failing the build on it would only stop people running it.
    }

    private static string? GradeValidatorNormalize(string? grade) =>
        grade is null ? null : Acordanza.Core.Models.GradeValidator.Normalize(grade);
}
