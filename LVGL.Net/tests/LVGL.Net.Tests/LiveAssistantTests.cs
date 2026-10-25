using Lvgl.Assistant;
using Lvgl.Assistant.Chat;
using Lvgl.Ui;

namespace Lvgl.Tests;

/// <summary>
/// Calls a real model through <see cref="AssistantService"/>: the provider connector, the tool loop
/// and the design plugin, end to end.
/// </summary>
/// <remarks>
/// Opt-in, because it needs an API key and costs a request: set <c>LVGLNET_LIVE_LLM=1</c> and
/// the provider's key variable (<c>ANTHROPIC_API_KEY</c> by default). <c>LVGLNET_LIVE_PROVIDER</c>
/// and <c>LVGLNET_LIVE_MODEL</c> choose another provider or model. Without the opt-in the test
/// returns immediately, so CI and machines without keys are unaffected.
/// </remarks>
public sealed class LiveAssistantTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lvglnet-live-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    private static bool Enabled => Environment.GetEnvironmentVariable("LVGLNET_LIVE_LLM") is "1" or "true";

    [Fact]
    public async Task The_model_designs_a_valid_layout_through_the_tool()
    {
        if (!Enabled) return;

        var provider = Enum.TryParse<AssistantProvider>(Environment.GetEnvironmentVariable("LVGLNET_LIVE_PROVIDER"), out var chosen)
            ? chosen
            : AssistantProvider.Anthropic;

        var options = new AssistantOptions
        {
            Provider = provider,
            SessionDirectory = _folder,
            AttachmentDirectory = Path.Combine(_folder, "attachments"),
            MaxToolIterations = 8,
        };

        if (Environment.GetEnvironmentVariable("LVGLNET_LIVE_MODEL") is { Length: > 0 } model) options.For(provider).Model = model;
        Assert.True(options.For(provider).IsConfigured, $"Set {options.For(provider).ApiKeyVariable} to run the live test.");

        using var assistant = new AssistantService(options);
        UiDocument? produced = null;
        assistant.LayoutProduced += (_, document) => produced = document;

        var session = assistant.Sessions.Create(provider, options.For(provider).Model);
        var reply = new System.Text.StringBuilder();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await foreach (var chunk in assistant.SendAsync(
            session,
            "Design a 480x320 thermostat screen: a title label, an Arc for the set point from 10 to 30, " +
            "and two buttons named WarmerButton and CoolerButton. Create it with the create_layout tool.",
            cancellationToken: timeout.Token))
        {
            reply.Append(chunk);
        }

        var last = session.Messages[^1];
        Assert.False(last.IsError, "The model call failed: " + last.Text);
        Assert.NotNull(produced);

        // The plugin only accepts valid documents, so this is the model's output after validation.
        Assert.Empty(produced!.Validate());
        Assert.Contains(produced.Descendants(), node => node.Type == UiWidgetType.Arc);
        Assert.NotNull(produced.Find("WarmerButton"));
        Assert.Contains(last.ToolCalls, call => call.Contains("create_layout", StringComparison.OrdinalIgnoreCase));

        // The generated code for what the model designed must compile in principle: no problems.
        Assert.False(string.IsNullOrWhiteSpace(new CSharpUiGenerator().Generate(produced)));
    }
}
