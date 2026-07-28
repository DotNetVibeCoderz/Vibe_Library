// Namespace is Providers, not Anthropic: the official SDK's root namespace is `Anthropic`, and a
// nested namespace of the same name makes every `using Anthropic;` in this folder ambiguous.
namespace Lvgl.Assistant.Providers;

/// <summary>
/// Per-model request-shape rules for the Claude API.
/// </summary>
/// <remarks>
/// <para>
/// Claude's request surface is not uniform across models, and the differences are hard errors
/// rather than ignored fields. The two that bite an application with a shared settings screen:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>Sampling parameters were removed.</b> Claude Opus 5, Opus 4.8, Opus 4.7 and Fable 5
///     reject <c>temperature</c>, <c>top_p</c> and <c>top_k</c> with HTTP 400. Older models still
///     accept them. A UI that exposes a temperature slider therefore cannot send it unconditionally.
///   </description></item>
///   <item><description>
///     <b>Thinking is configured differently.</b> The fixed <c>budget_tokens</c> form is rejected
///     on those same models; adaptive thinking replaces it, and on Opus 5 it is on by default.
///   </description></item>
/// </list>
/// <para>
/// Matching is by id prefix rather than an exact list so a new dated snapshot of a known family
/// keeps the right behaviour instead of silently falling back to the permissive path.
/// </para>
/// </remarks>
public static class AnthropicModelTraits
{
    // Families that removed temperature/top_p/top_k. Ordered longest-prefix-first is unnecessary
    // here because no entry is a prefix of another.
    private static readonly string[] NoSamplingParameters =
    [
        "claude-opus-5",
        "claude-opus-4-8",
        "claude-opus-4-7",
        "claude-fable-5",
        "claude-mythos-5",
        "claude-mythos-preview",
        "claude-sonnet-5",
    ];

    /// <summary>
    /// True when the model accepts <c>temperature</c>. When false, sending it returns HTTP 400 and
    /// the value must be dropped from the request.
    /// </summary>
    public static bool SupportsTemperature(string? modelId) =>
        !MatchesFamily(modelId, NoSamplingParameters);

    /// <summary>
    /// True when the model is from a family that uses adaptive thinking rather than a fixed
    /// thinking-token budget.
    /// </summary>
    public static bool UsesAdaptiveThinking(string? modelId) =>
        MatchesFamily(modelId, NoSamplingParameters) ||
        MatchesFamily(modelId, ["claude-opus-4-6", "claude-sonnet-4-6"]);

    /// <summary>
    /// A short, user-facing explanation of why a configured temperature was ignored, or null when
    /// it was honoured.
    /// </summary>
    public static string? ExplainIgnoredTemperature(string? modelId) =>
        SupportsTemperature(modelId)
            ? null
            : $"{modelId} does not accept a temperature - Anthropic removed the sampling " +
              "parameters on this model family, and sending one is rejected. The configured " +
              "value was left out of the request; use the effort setting to trade quality " +
              "against cost instead.";

    private static bool MatchesFamily(string? modelId, IEnumerable<string> families)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return false;

        var id = modelId.Trim().ToLowerInvariant();

        // A bare family id or a dated snapshot of it ("claude-opus-4-7-20260101") both match;
        // an unrelated id that merely starts with the same letters does not, because every
        // family entry ends at a component boundary.
        return families.Any(family => id == family || id.StartsWith(family + "-", StringComparison.Ordinal));
    }
}
