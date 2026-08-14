using System.Text.Json;
using Acordanza.Worker.Services;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;
using Shouldly;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// Sets output_config.effort on a model that doesn't support it and the Messages API
// 400s ("This model does not support the effort parameter") — an error that only ever
// showed up in production. Since plan 32 every one of the five Claude providers builds its
// request through the one shared AnthropicChatExtensions.WithAnthropicRequest, so asserting
// the helper against the known effort-support table (ClaudeModelCapabilities) covers all
// five call sites rather than the two that had a BuildRequestParams to poke at before.
// Pure unit tier — no Docker, no network, just inspecting the constructed request.
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

    // The models the five providers actually pin, plus one that predates the current pins:
    // a re-pin onto a model that rejects `effort` fails here instead of in production.
    [Theory]
    [InlineData("claude-opus-5", true)]
    [InlineData("claude-sonnet-5", true)]
    [InlineData("claude-haiku-4-5", false)]
    [InlineData("claude-sonnet-4-5", false)]
    public void WithAnthropicRequest_never_sets_effort_on_a_model_that_rejects_it(string model, bool expectEffort)
    {
        var request = RawRequestFor(new ChatOptions { ModelId = model, MaxOutputTokens = 1024 }
            .WithAnthropicRequest(model, Effort.Medium));

        HasEffort(request).ShouldBe(expectEffort);
    }

    // The other half of the same guard: passing no effort at all must not conjure one, even
    // on a model that would accept it.
    [Fact]
    public void WithAnthropicRequest_sets_no_effort_when_none_is_asked_for()
    {
        var request = RawRequestFor(new ChatOptions { ModelId = "claude-opus-5", MaxOutputTokens = 1024 }
            .WithAnthropicRequest("claude-opus-5", effort: null));

        request.OutputConfig.ShouldBeNull();
    }

    // MaxOutputTokens is not optional: FoundryClient.CreateChatClient configures no default
    // for it (five providers, five budgets), and max_tokens is required on the wire, so an
    // omitted one has to fail loudly here rather than at the first real Foundry call.
    [Fact]
    public void WithAnthropicRequest_refuses_options_with_no_token_budget()
    {
        Should.Throw<InvalidOperationException>(() =>
            new ChatOptions { ModelId = "claude-opus-5" }.WithAnthropicRequest("claude-opus-5", Effort.Low));
    }

    // Every provider's options, built exactly as the provider builds them, must carry both
    // the model id and the token budget — §2's rule, asserted rather than code-reviewed.
    public static TheoryData<string, ChatOptions> ProviderOptions() => new()
    {
        { ClaudeRouteEnrichmentProvider.Model, ClaudeRouteEnrichmentProvider.BuildOptions() },
        { ClaudeTopoVerificationProvider.Model, ClaudeTopoVerificationProvider.BuildOptions() },
        { ClaudeTopoImportGateProvider.Model, ClaudeTopoImportGateProvider.BuildOptions() },
        { ClaudeTopoExtractionProvider.Model, ClaudeTopoExtractionProvider.BuildOptions() },
        { ClaudePageExtractionProvider.Model, ClaudePageExtractionProvider.BuildOptions() },
    };

    [Theory]
    [MemberData(nameof(ProviderOptions))]
    public void Every_provider_pins_its_model_and_token_budget_explicitly(string model, ChatOptions options)
    {
        options.ModelId.ShouldBe(model);
        options.MaxOutputTokens.ShouldNotBeNull();
        HasEffort(RawRequestFor(options)).ShouldBe(ClaudeModelCapabilities.SupportsEffort(model));
    }

    private static MessageCreateParams RawRequestFor(ChatOptions options) =>
        (MessageCreateParams)options.RawRepresentationFactory!(null!)!;

    // Serializes to the actual wire JSON rather than inspecting properties directly —
    // OutputConfig.Effort is a non-nullable ApiEnum struct, so "was it set" can only be
    // observed through whether the serializer emits the "effort" key at all. A null
    // OutputConfig (the guard's skip path) serializes to "null", which has no properties.
    private static bool HasEffort(MessageCreateParams request) =>
        request.OutputConfig is { } config
        && JsonDocument.Parse(JsonSerializer.Serialize(config)).RootElement.TryGetProperty("effort", out _);
}
