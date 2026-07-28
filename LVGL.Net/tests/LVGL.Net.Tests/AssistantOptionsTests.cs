using System.Collections.Specialized;
using Lvgl.Assistant;

namespace Lvgl.Tests;

public class AssistantOptionsTests
{
    private static NameValueCollection Settings(params (string Key, string Value)[] entries)
    {
        var collection = new NameValueCollection();
        foreach (var (key, value) in entries) collection[AssistantOptions.Prefix + key] = value;
        return collection;
    }

    [Fact]
    public void An_empty_config_yields_usable_defaults()
    {
        var options = AssistantOptions.Load(new NameValueCollection());

        Assert.Equal("Jack The Code Bender", options.Name);
        Assert.Equal(AssistantProvider.OpenAI, options.Provider);
        Assert.Equal(0.7, options.Temperature);
        Assert.True(options.EnableFunctionCalling);
        Assert.False(string.IsNullOrWhiteSpace(options.SystemPrompt));
    }

    [Fact]
    public void Settings_are_read_from_the_config_section()
    {
        var options = AssistantOptions.Load(Settings(
            ("Name", "Jack"),
            ("Provider", "anthropic"),
            ("Temperature", "1.25"),
            ("MaxTokens", "8192"),
            ("EnableFunctionCalling", "false"),
            ("Anthropic:Model", "claude-sonnet-5")));

        Assert.Equal("Jack", options.Name);
        Assert.Equal(AssistantProvider.Anthropic, options.Provider);
        Assert.Equal(1.25, options.Temperature);
        Assert.Equal(8192, options.MaxTokens);
        Assert.False(options.EnableFunctionCalling);
        Assert.Equal("claude-sonnet-5", options.Anthropic.Model);
    }

    [Theory]
    [InlineData("-3", 0)]
    [InlineData("9", 2)]
    public void Temperature_is_clamped_to_the_valid_range(string configured, double expected)
    {
        Assert.Equal(expected, AssistantOptions.Load(Settings(("Temperature", configured))).Temperature);
    }

    [Fact]
    public void Unparseable_values_leave_the_default_rather_than_throwing()
    {
        // A hand-edited config with a typo should degrade, not stop the designer from starting.
        var options = AssistantOptions.Load(Settings(
            ("Temperature", "warm"),
            ("MaxTokens", "lots"),
            ("Provider", "Telepathy")));

        Assert.Equal(0.7, options.Temperature);
        Assert.Equal(4096, options.MaxTokens);
        Assert.Equal(AssistantProvider.OpenAI, options.Provider);
    }

    [Fact]
    public void An_environment_variable_beats_a_key_in_the_config_file()
    {
        // The point being that a committed app.config can be left blank.
        const string variable = "LVGLNET_TEST_OPENAI_KEY";
        Environment.SetEnvironmentVariable(variable, "from-environment");

        try
        {
            var options = AssistantOptions.Load(Settings(("OpenAI:ApiKey", "from-config")));
            var provider = options.OpenAI with { ApiKeyVariable = variable };
            provider.ApiKey = "from-config";

            Assert.Equal("from-environment", provider.ResolveApiKey());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void The_config_key_is_used_when_no_environment_variable_is_set()
    {
        var options = AssistantOptions.Load(Settings(("OpenAI:ApiKey", "from-config")));

        var provider = options.OpenAI with { ApiKeyVariable = "LVGLNET_TEST_UNSET_KEY" };
        provider.ApiKey = "from-config";

        Assert.Equal("from-config", provider.ResolveApiKey());
        Assert.True(provider.IsConfigured);
    }

    [Fact]
    public void Ollama_counts_as_configured_without_a_key()
    {
        // It runs locally, so requiring a key would wrongly hide it from the provider list.
        var options = AssistantOptions.Load(new NameValueCollection());

        Assert.Null(options.Ollama.ApiKeyVariable);
        Assert.True(options.Ollama.IsConfigured);
        Assert.Contains(AssistantProvider.Ollama, options.ConfiguredProviders());
    }

    [Fact]
    public void For_returns_the_settings_of_each_provider()
    {
        var options = AssistantOptions.Load(new NameValueCollection());

        Assert.Same(options.OpenAI, options.For(AssistantProvider.OpenAI));
        Assert.Same(options.Anthropic, options.For(AssistantProvider.Anthropic));
        Assert.Same(options.Gemini, options.For(AssistantProvider.Gemini));
        Assert.Same(options.Ollama, options.For(AssistantProvider.Ollama));
    }
}
