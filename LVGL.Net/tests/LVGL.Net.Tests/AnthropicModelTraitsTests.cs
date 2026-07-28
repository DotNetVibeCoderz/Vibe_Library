using Lvgl.Assistant.Providers;

namespace Lvgl.Tests;

/// <summary>
/// Guards the per-model request rules for the Claude API.
/// </summary>
/// <remarks>
/// This matters because the failure is a hard HTTP 400 rather than an ignored field: sending a
/// temperature to a model family that removed the sampling parameters fails the whole request. The
/// designer exposes a temperature slider that applies to every provider, so the connector has to
/// know which models can actually receive it.
/// </remarks>
public class AnthropicModelTraitsTests
{
    [Theory]
    [InlineData("claude-opus-5")]
    [InlineData("claude-opus-4-8")]
    [InlineData("claude-opus-4-7")]
    [InlineData("claude-sonnet-5")]
    [InlineData("claude-fable-5")]
    [InlineData("claude-mythos-5")]
    public void Current_families_reject_temperature(string modelId)
    {
        Assert.False(AnthropicModelTraits.SupportsTemperature(modelId));
        Assert.NotNull(AnthropicModelTraits.ExplainIgnoredTemperature(modelId));
    }

    [Theory]
    [InlineData("claude-opus-4-6")]
    [InlineData("claude-sonnet-4-6")]
    [InlineData("claude-haiku-4-5")]
    [InlineData("claude-sonnet-4-5")]
    public void Older_families_still_accept_temperature(string modelId)
    {
        Assert.True(AnthropicModelTraits.SupportsTemperature(modelId));
        Assert.Null(AnthropicModelTraits.ExplainIgnoredTemperature(modelId));
    }

    [Theory]
    [InlineData("claude-opus-4-7-20260101")]
    [InlineData("claude-sonnet-5-20260315")]
    public void A_dated_snapshot_inherits_its_family_rules(string modelId)
    {
        // Matching on the family prefix means a new snapshot does not silently fall through to
        // the permissive path and start returning 400s.
        Assert.False(AnthropicModelTraits.SupportsTemperature(modelId));
    }

    [Theory]
    [InlineData("claude-opus-50")]
    [InlineData("claude-opus-4-70-experimental")]
    public void A_longer_id_that_merely_starts_the_same_is_not_a_match(string modelId)
    {
        // "claude-opus-5" must not match "claude-opus-50": family entries end at a component
        // boundary, so a substring is not enough.
        Assert.True(AnthropicModelTraits.SupportsTemperature(modelId));
    }

    [Theory]
    [InlineData("CLAUDE-OPUS-5")]
    [InlineData("  claude-opus-5  ")]
    public void Matching_ignores_case_and_surrounding_whitespace(string modelId)
    {
        Assert.False(AnthropicModelTraits.SupportsTemperature(modelId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unknown_model_is_treated_permissively(string? modelId)
    {
        // Better to send the parameter and get a clear API error than to silently drop a setting
        // for a model we simply have not heard of.
        Assert.True(AnthropicModelTraits.SupportsTemperature(modelId));
    }

    [Fact]
    public void The_explanation_names_the_model_and_says_what_to_do_instead()
    {
        var explanation = AnthropicModelTraits.ExplainIgnoredTemperature("claude-opus-5");

        Assert.NotNull(explanation);
        Assert.Contains("claude-opus-5", explanation, StringComparison.Ordinal);
        Assert.Contains("effort", explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("claude-opus-5", true)]
    [InlineData("claude-opus-4-6", true)]
    [InlineData("claude-sonnet-4-6", true)]
    [InlineData("claude-haiku-4-5", false)]
    public void Adaptive_thinking_families_are_recognised(string modelId, bool expected)
    {
        Assert.Equal(expected, AnthropicModelTraits.UsesAdaptiveThinking(modelId));
    }
}
