using Lvgl.Widgets;

namespace Lvgl.Samples.Desktop.Demos;

/// <summary>
/// A live chart fed from the run loop, which is the pattern the Raspberry Pi sample uses for real
/// sensor data.
/// </summary>
internal sealed class ChartDemo : DemoPage
{
    private const int PointCount = 120;

    private LvChart? _chart;
    private LvChartSeries? _sine;
    private LvChartSeries? _noise;
    private LvLabel? _readout;
    private LvSlider? _speed;
    private readonly Random _random = new(1);
    private long _lastSampleMs;
    private double _phase;

    public override string Title => "Live chart";

    public override string Description => "Streaming data with LvChartUpdateMode.Shift";

    public override void Build(LvObject container, LvglApplication application)
    {
        // Sizes are computed once into locals rather than read back off each widget after
        // creating it. A freshly created widget reports 0 until layout runs, so `card.Width - 60`
        // would silently produce a negative size and the chart would never appear.
        var contentWidth = Math.Max(240, container.Width - 32);

        var card = new LvPanel(container, contentWidth, 300);
        card.Align(LvAlign.TopLeft);
        card.SetBackgroundColor(Theme.Surface);
        card.SetBorderWidth(0);
        card.SetRadius(12);
        card.SetPadding(16);

        _chart = new LvChart(card);
        _chart.SetSize(contentWidth - 60, 220);
        _chart.Align(LvAlign.TopLeft, 0, 24);
        _chart.Type = LvChartType.Line;
        _chart.PointCount = PointCount;
        _chart.UpdateMode = LvChartUpdateMode.Shift;
        _chart.SetRange(LvChartAxis.PrimaryY, -100, 100);
        _chart.SetDivisionLines(5, 8);
        _chart.SetBackgroundColor(Theme.Background);
        _chart.SetBorderWidth(0);
        _chart.SetRadius(8);
        _chart.SetLineColor(Theme.SurfaceAlt, LvPart.Items);

        _sine = _chart.AddSeries(Theme.Accent);
        _noise = _chart.AddSeries(Theme.AccentWarm);

        // Priming both series avoids the ramp-in from zero that an empty chart would show.
        _sine.Fill(0);
        _noise.Fill(0);

        var heading = new LvLabel(card, "Signal");
        heading.Align(LvAlign.TopLeft);
        heading.SetTextColor(Theme.TextMuted);
        heading.SetFontSize(12);

        _readout = new LvLabel(card, string.Empty);
        _readout.Align(LvAlign.TopRight);
        _readout.SetTextColor(Theme.Accent);
        _readout.SetFontSize(16);

        var controls = new LvPanel(container, contentWidth, 90);
        controls.Align(LvAlign.TopLeft, 0, 312);
        controls.SetBackgroundColor(Theme.Surface);
        controls.SetBorderWidth(0);
        controls.SetRadius(12);
        controls.SetPadding(16);

        var speedLabel = new LvLabel(controls, "Sample interval");
        speedLabel.Align(LvAlign.TopLeft);
        speedLabel.SetTextColor(Theme.TextMuted);
        speedLabel.SetFontSize(12);

        _speed = new LvSlider(controls);
        _speed.SetSize(contentWidth - 60, 8);
        _speed.Align(LvAlign.BottomLeft, 0, -8);
        _speed.SetRange(16, 250);
        _speed.SetValue(50, animate: false);
        _speed.SetBackgroundColor(Theme.Accent, LvPart.Indicator);
        _speed.SetBackgroundColor(Theme.Accent, LvPart.Knob);

        _lastSampleMs = Environment.TickCount64;
    }

    public override void Update(LvglApplication application)
    {
        if (_chart is not { IsAlive: true } || _sine is null || _noise is null) return;

        var interval = _speed is { IsAlive: true } ? _speed.Value : 50;
        var now = Environment.TickCount64;
        if (now - _lastSampleMs < interval) return;

        _lastSampleMs = now;
        _phase += 0.08;

        var sine = (int)(Math.Sin(_phase) * 80);
        var noise = (int)(Math.Sin(_phase * 0.37) * 45 + _random.Next(-12, 13));

        _sine.AddPoint(sine);
        _noise.AddPoint(noise);

        if (_readout is { IsAlive: true }) _readout.Text = $"{sine,4} / {noise,4}";
    }

    public override void Teardown()
    {
        _chart = null;
        _sine = null;
        _noise = null;
        _readout = null;
        _speed = null;
    }
}
