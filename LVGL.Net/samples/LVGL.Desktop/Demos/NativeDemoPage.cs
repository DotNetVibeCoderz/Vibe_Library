using Lvgl.Widgets;

namespace Lvgl.Samples.Desktop.Demos;

/// <summary>
/// Runs one of LVGL's own bundled demo scenes, as a check that the native build is complete and as
/// a rendering performance baseline.
/// </summary>
/// <remarks>
/// These scenes build their widgets in C and expect to own a whole screen, so this page loads them
/// onto a separate <see cref="LvScreen"/> and returns to the browser when the user presses Back.
/// </remarks>
internal sealed class NativeDemoPage : DemoPage
{
    private LvScreen? _demoScreen;

    public override string Title => "LVGL demos";

    public override string Description => "The widget showcase and benchmark bundled with LVGL itself";

    public override void Build(LvObject container, LvglApplication application)
    {
        var card = new LvPanel(container, Math.Max(240, container.Width - 32), 210);
        card.SetBackgroundColor(Theme.Surface);
        card.SetBorderWidth(0);
        card.SetRadius(12);
        card.SetPadding(16);

        var heading = new LvLabel(card, "Run a bundled LVGL scene");
        heading.Align(LvAlign.TopLeft);
        heading.SetTextColor(Theme.TextMuted);
        heading.SetFontSize(12);

        var note = new LvLabel(card,
            "These are built in C and take over the screen.\nPress Back to return to this browser.");
        note.Align(LvAlign.TopLeft, 0, 28);
        note.SetTextColor(Theme.Text);

        var widgets = new LvButton(card, "Widget showcase");
        widgets.SetSize(200, 46);
        widgets.Align(LvAlign.BottomLeft);
        widgets.SetRadius(8);
        widgets.SetBackgroundColor(Theme.Accent);
        widgets.SetTextColor(Theme.Background);
        widgets.Clicked += (_, _) => RunNativeDemo(application, LvglDemos.ShowWidgets, "widget showcase");

        var benchmark = new LvButton(card, "Benchmark");
        benchmark.SetSize(200, 46);
        benchmark.Align(LvAlign.BottomLeft, 216, 0);
        benchmark.SetRadius(8);
        benchmark.SetBackgroundColor(Theme.SurfaceAlt);
        benchmark.Clicked += (_, _) => RunNativeDemo(application, LvglDemos.ShowBenchmark, "benchmark");

        var status = new LvLabel(container, string.Empty);
        status.Align(LvAlign.TopLeft, 0, 226);
        status.SetTextColor(Theme.TextMuted);
        status.Text = $"LVGL {LvglRuntime.LvglVersion}, {LvglRuntime.ColorDepth}-bit colour";
    }

    private void RunNativeDemo(LvglApplication application, Func<bool> start, string name)
    {
        var previous = LvScreen.Active();

        _demoScreen = LvScreen.Create();
        _demoScreen.Load();

        if (!start())
        {
            _demoScreen.Delete();
            _demoScreen = null;
            previous.Load();

            var warning = new LvLabel(previous, $"{LvSymbols.Warning} The native library was built without the {name}.");
            warning.Align(LvAlign.BottomMid, 0, -12);
            warning.SetTextColor(Theme.Danger);
            return;
        }

        // A floating Back button on top of whatever the demo drew.
        var back = new LvButton(_demoScreen, $"{LvSymbols.Left} Back");
        back.SetSize(110, 40);
        back.Align(LvAlign.TopRight, -8, 8);
        back.SetRadius(8);
        back.Clicked += (_, _) =>
        {
            previous.Load();

            // Deleting the screen from inside its own event handler is safe - LVGL defers the
            // free - but the managed reference must be dropped here.
            var screen = _demoScreen;
            _demoScreen = null;
            screen?.Delete();
        };
    }

    public override void Teardown()
    {
        _demoScreen?.Delete();
        _demoScreen = null;
    }
}
