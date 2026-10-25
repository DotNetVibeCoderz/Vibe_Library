using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lvgl.Designer.Views;
using Lvgl.Ui;

namespace Lvgl.Designer.Tests;

/// <summary>
/// Renders the designer screenshots used in the README and docs, from the showcase layout in
/// samples/layouts. Writes only when <c>LVGLNET_DOC_SCREENSHOTS</c> names the output folder, so the
/// images are regenerated deliberately - and always from the current code, never by hand.
/// </summary>
public class DocumentationScreenshots
{
    private static readonly string? Folder = Environment.GetEnvironmentVariable("LVGLNET_DOC_SCREENSHOTS");

    [AvaloniaFact]
    public void Designer_views()
    {
        if (string.IsNullOrEmpty(Folder)) return;

        var layout = FindShowcase();
        Assert.True(layout is not null, "samples/layouts/smart-home.lvgl.json not found.");

        var window = new MainWindow { Width = 1600, Height = 940 };
        window.Show();
        Pump(150);

        window.Session.Load(UiJson.Load(layout!), layout);
        window.FindControl<BoardCanvas>("Canvas")!.FitToView();
        Pump(150);
        Assert.Empty(window.Session.Document.Validate());

        window.Session.Selection = window.Session.Document.Find("ThermostatArc");
        Pump(120);
        Save(window, "designer-design.png");

        window.FindControl<InspectorPanel>("Inspector")!.ShowStyles(window.Session.Document.FindStyle("Card"));
        Pump(80);
        Save(window, "designer-styles.png");

        window.FindControl<RadioButton>("CodeModeButton")!.IsChecked = true;
        Pump(80);
        Save(window, "designer-code.png");

        window.Close();
    }

    private static void Save(Window window, string name)
    {
        Pump(60);
        window.CaptureRenderedFrame()!.Save(Path.Combine(Folder!, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static void Pump(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        while (clock.ElapsedMilliseconds < milliseconds);
    }

    private static string? FindShowcase()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "samples", "layouts", "smart-home.lvgl.json");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
