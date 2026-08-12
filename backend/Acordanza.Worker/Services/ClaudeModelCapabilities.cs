namespace Acordanza.Worker.Services;

// The `effort` field in OutputConfig 400s on models that don't support it — a
// runtime-only failure (see IRouteEnrichmentProvider.cs's providers, both of
// which pin a model for cost/quota reasons and must know whether that model
// tolerates effort). This table lets ClaudeModelCapabilitiesTests catch a bad
// (model, effort) pairing at `dotnet test` time instead of in production logs.
// Source: Anthropic's effort-parameter support table (Claude API docs) as of
// 2026 — re-check that table before adding a model here.
public static class ClaudeModelCapabilities
{
    private static readonly HashSet<string> ModelsSupportingEffort =
    [
        "claude-opus-5", "claude-sonnet-5", "claude-fable-5", "claude-mythos-5",
        "claude-opus-4-8", "claude-opus-4-7", "claude-opus-4-6", "claude-sonnet-4-6",
        "claude-opus-4-5"
    ];

    public static bool SupportsEffort(string model) => ModelsSupportingEffort.Contains(model);
}
