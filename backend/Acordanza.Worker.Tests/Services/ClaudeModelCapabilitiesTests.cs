using System.Text.Json;
using Acordanza.Worker.Services;
using Anthropic.Models.Messages;
using Shouldly;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// Sets output_config.effort on a model that doesn't support it and the Messages API
// 400s ("This model does not support the effort parameter") — an error that only
// ever showed up in production. This asserts both Claude providers' actual request
// params against the known effort-support table (ClaudeModelCapabilities), so a
// model pin or an unconditional Effort assignment that would 400 fails here first.
// Pure unit tier — no Docker, no network, just inspecting the constructed params.
public class ClaudeModelCapabilitiesTests
{
    [Theory]
    [InlineData("claude-sonnet-4-5", false)]
    [InlineData("claude-haiku-4-5", false)]
    [InlineData("claude-sonnet-5", true)]
    [InlineData("claude-opus-5", true)]
    [InlineData("claude-sonnet-4-6", true)]
    public void SupportsEffort_matches_the_known_table(string model, bool expected)
    {
        ClaudeModelCapabilities.SupportsEffort(model).ShouldBe(expected);
    }

    [Fact]
    public void Route_enrichment_request_never_sets_effort_on_a_model_that_rejects_it()
    {
        var request = ClaudeRouteEnrichmentProvider.BuildRequestParams("prompt");

        request.Model.Raw().ShouldBe(ClaudeRouteEnrichmentProvider.Model);
        AssertEffortMatchesCapability(request.Model.Raw(), request.OutputConfig);
    }

    [Fact]
    public void Topo_verification_request_never_sets_effort_on_a_model_that_rejects_it()
    {
        var imageBlock = new ContentBlockParam(new TextBlockParam { Text = "image placeholder" });
        var textBlock = new ContentBlockParam(new TextBlockParam { Text = "text" });

        var request = ClaudeTopoVerificationProvider.BuildRequestParams(imageBlock, textBlock);

        request.Model.Raw().ShouldBe(ClaudeTopoVerificationProvider.Model);
        AssertEffortMatchesCapability(request.Model.Raw(), request.OutputConfig);
    }

    // Serializes to the actual wire JSON rather than inspecting properties directly —
    // OutputConfig.Effort is a non-nullable ApiEnum struct, so "was it set" can only be
    // observed through whether the serializer emits the "effort" key at all.
    private static void AssertEffortMatchesCapability(string model, OutputConfig? outputConfig)
    {
        var json = JsonSerializer.Serialize(outputConfig);
        var hasEffort = JsonDocument.Parse(json).RootElement.TryGetProperty("effort", out _);
        hasEffort.ShouldBe(ClaudeModelCapabilities.SupportsEffort(model));
    }
}
