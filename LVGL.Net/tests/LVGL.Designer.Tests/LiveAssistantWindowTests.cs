using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lvgl.Assistant;
using Lvgl.Designer.Assistant;

namespace Lvgl.Designer.Tests;

/// <summary>
/// The designer's chat panel against a real model: the request streams into the transcript and
/// the layout Jack produces lands on the canvas. Opt-in like the library's live test - set
/// <c>LVGLNET_LIVE_LLM=1</c> and the provider's key.
/// </summary>
public sealed class LiveAssistantWindowTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lvglnet-live-ui-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    [AvaloniaFact]
    public async Task Jack_designs_a_screen_onto_the_canvas()
    {
        if (Environment.GetEnvironmentVariable("LVGLNET_LIVE_LLM") is not ("1" or "true")) return;

        ChatPanel.DisposeShared();
        ChatPanel.OptionsFactory = () =>
        {
            var options = new AssistantOptions
            {
                Provider = AssistantProvider.Anthropic,
                SessionDirectory = _folder,
                AttachmentDirectory = Path.Combine(_folder, "attachments"),
            };

            if (Environment.GetEnvironmentVariable("LVGLNET_LIVE_MODEL") is { Length: > 0 } model) options.Anthropic.Model = model;
            return options;
        };

        var window = new MainWindow { Width = 1600, Height = 960 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.FindControl<ToggleButton>("AssistantToggle")!.IsChecked = true;
        window.FindControl<ToggleButton>("AssistantToggle")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var panel = window.FindControl<ChatPanel>("Assistant")!;
        var composer = panel.GetVisualDescendants().OfType<TextBox>().Single(t => t.AcceptsReturn);
        composer.Text = "Design a 800x480 smart-home dashboard for the Pi display: a title, three room cards with an Led " +
                        "and a Switch each, and an Arc for the thermostat. Put it on my canvas with create_layout.";

        var send = panel.SendAsync();
        var clock = Stopwatch.StartNew();
        while (!send.IsCompleted && clock.Elapsed < TimeSpan.FromMinutes(4))
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }

        await send;
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.NotEqual("MainScreen", window.Session.Document.Name);
        Assert.Contains(window.Session.Document.Descendants(), n => n.Type == Lvgl.Ui.UiWidgetType.Arc);
        Assert.True(window.Session.CanUndo, "Jack's layout should be one undo away.");

        if (Environment.GetEnvironmentVariable("LVGLNET_SCREENSHOTS") is { Length: > 0 } shots)
        {
            Directory.CreateDirectory(shots);
            window.CaptureRenderedFrame()?.Save(Path.Combine(shots, "08-live-jack.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        if (Environment.GetEnvironmentVariable("LVGLNET_DOC_SCREENSHOTS") is { Length: > 0 } docs)
        {
            window.FindControl<Lvgl.Designer.Views.BoardCanvas>("Canvas")!.FitToView();
            for (var i = 0; i < 20; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }

            window.CaptureRenderedFrame()?.Save(Path.Combine(docs, "designer-assistant.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        window.Close();
        ChatPanel.DisposeShared();
    }
}
