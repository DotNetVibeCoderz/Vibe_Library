using BenchmarkDotNet.Attributes;
using Lvgl.Backends;
using Lvgl.Drawing;
using Lvgl.Widgets;

namespace Lvgl.Benchmarks;

/// <summary>
/// The calls a live dashboard makes every frame, measured for time and managed allocation.
/// </summary>
/// <remarks>
/// LVGL is owned by the thread that initialises it; BenchmarkDotNet runs the setup and the
/// benchmarks on the same thread of its child process, which satisfies that.
/// Measured: zero bytes allocated for every one of them - including <see cref="SetLabelText"/>,
/// because <c>[LibraryImport]</c> marshals a short string to UTF-8 on the stack.
/// </remarks>
[MemoryDiagnoser]
public class RenderPathBenchmarks
{
    private HeadlessBackend _backend = null!;
    private LvglApplication _application = null!;
    private LvBar _bar = null!;
    private LvChartSeries _series = null!;
    private LvPanel _panel = null!;
    private LvLabel _label = null!;
    private int _value;

    [GlobalSetup]
    public void Setup()
    {
        _backend = new HeadlessBackend(800, 480);
        _application = LvglApplication.Create(_backend, new LvglOptions { RenderMode = LvRenderMode.Partial, PartialBufferLines = 32 });

        var screen = _application.Screen;
        _bar = new LvBar(screen);
        _bar.SetSize(400, 24);

        var chart = new LvChart(screen) { PointCount = 100, UpdateMode = LvChartUpdateMode.Shift };
        chart.SetPosition(0, 60);
        chart.SetSize(780, 300);
        _series = chart.AddSeries(LvColor.FromRgb(0x38BDF8u));

        _panel = new LvPanel(screen, 120, 80);
        _panel.SetPosition(420, 380);

        _label = new LvLabel(screen, "0");
        _label.SetPosition(10, 400);

        _application.Display.RefreshNow();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _application.Dispose();
        _backend.Dispose();
    }

    /// <summary>One chart sample - the per-sample cost of a live trace.</summary>
    [Benchmark]
    public void ChartAddPoint() => _series.AddPoint(_value++ % 100);

    /// <summary>A value change on a widget.</summary>
    [Benchmark]
    public void SetBarValue() => _bar.SetValue(_value++ % 100, animate: false);

    /// <summary>A colour change, through the lv_color_t ABI shim.</summary>
    [Benchmark]
    public void SetBackgroundColor() => _panel.SetBackgroundColor(LvColor.FromRgb((uint)(_value++ * 0x10101) & 0xFFFFFF));

    /// <summary>A run-loop pass with nothing to redraw: timers and input only.</summary>
    [Benchmark]
    public void RunFrameIdle() => _application.RunFrame();

    /// <summary>Invalidating and rendering the whole 800x480 screen, flush included.</summary>
    [Benchmark]
    public void RedrawFullScreen()
    {
        _application.Screen.Invalidate();
        _application.Display.RefreshNow();
    }

    /// <summary>Setting text marshals the string to UTF-8 - on the stack for short strings, so no heap allocation.</summary>
    [Benchmark]
    public void SetLabelText() => _label.Text = "42";
}
