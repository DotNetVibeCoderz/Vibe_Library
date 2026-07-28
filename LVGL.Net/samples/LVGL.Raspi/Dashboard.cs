using System.Globalization;
using Lvgl.Drawing;
using Lvgl.Samples.Raspi.Sensors;
using Lvgl.Widgets;

namespace Lvgl.Samples.Raspi;

/// <summary>
/// The instrument panel: three live gauges above a rolling chart of load and temperature.
/// </summary>
/// <remarks>
/// Every widget is created once, in <see cref="Build"/>. <see cref="Update"/> only assigns new
/// values, so a sample costs a handful of native calls and allocates nothing beyond the strings
/// LVGL needs for the labels. That matters on a Pi driving a panel for days at a time.
/// </remarks>
internal sealed class Dashboard
{
    private const int ChartPoints = 90;

    private static readonly LvColor Background = LvColor.FromRgb(0x0B1220u);
    private static readonly LvColor Surface = LvColor.FromRgb(0x16202Fu);
    private static readonly LvColor Text = LvColor.FromRgb(0xE8EEF6u);
    private static readonly LvColor Muted = LvColor.FromRgb(0x7E8C9Fu);
    private static readonly LvColor LoadColor = LvColor.FromRgb(0x38BDF8u);
    private static readonly LvColor TempColor = LvColor.FromRgb(0xFB7185u);
    private static readonly LvColor MemoryColor = LvColor.FromRgb(0xA78BFAu);

    private readonly int _width;
    private readonly int _height;
    private readonly string _sourceDescription;

    private LvArc _loadArc = null!;
    private LvLabel _loadValue = null!;
    private LvArc _tempArc = null!;
    private LvLabel _tempValue = null!;
    private LvBar _memoryBar = null!;
    private LvLabel _memoryValue = null!;
    private LvLabel _uptime = null!;
    private LvLabel _status = null!;
    private LvChart _chart = null!;
    private LvChartSeries _loadSeries = null!;
    private LvChartSeries _tempSeries = null!;

    public Dashboard(int width, int height, string sourceDescription)
    {
        _width = width;
        _height = height;
        _sourceDescription = sourceDescription;
    }

    public void Build(LvObject screen)
    {
        screen.SetBackgroundColor(Background);
        screen.SetPadding(0);
        screen.IsScrollable = false;

        BuildHeader(screen);

        var gaugeTop = 64;
        var gaugeHeight = Math.Max(150, (_height - gaugeTop - 24) * 40 / 100);
        var cardWidth = (_width - 48) / 3;

        BuildLoadCard(screen, 16, gaugeTop, cardWidth, gaugeHeight);
        BuildTemperatureCard(screen, 24 + cardWidth, gaugeTop, cardWidth, gaugeHeight);
        BuildMemoryCard(screen, 32 + cardWidth * 2, gaugeTop, cardWidth, gaugeHeight);

        BuildChart(screen, 16, gaugeTop + gaugeHeight + 12, _width - 32, _height - gaugeTop - gaugeHeight - 28);
    }

    private void BuildHeader(LvObject screen)
    {
        var title = new LvLabel(screen, $"{LvSymbols.Charge} Raspberry Pi telemetry");
        title.Align(LvAlign.TopLeft, 16, 14);
        title.SetTextColor(Text);
        title.SetFontSize(20);

        _status = new LvLabel(screen, _sourceDescription);
        _status.Align(LvAlign.TopLeft, 16, 40);
        _status.SetTextColor(Muted);
        _status.SetFontSize(12);

        _uptime = new LvLabel(screen, "up --:--:--");
        _uptime.Align(LvAlign.TopRight, -16, 20);
        _uptime.SetTextColor(Muted);
        _uptime.SetFontSize(14);
    }

    private LvPanel Card(LvObject parent, string title, int x, int y, int width, int height)
    {
        var card = new LvPanel(parent, width, height);
        card.SetPosition(x, y);
        card.SetBackgroundColor(Surface);
        card.SetBorderWidth(0);
        card.SetRadius(14);
        card.SetPadding(12);
        card.IsScrollable = false;

        var heading = new LvLabel(card, title);
        heading.Align(LvAlign.TopLeft);
        heading.SetTextColor(Muted);
        heading.SetFontSize(12);

        return card;
    }

    private void BuildLoadCard(LvObject parent, int x, int y, int width, int height)
    {
        var card = Card(parent, "CPU LOAD", x, y, width, height);
        var diameter = Math.Min(width - 40, height - 60);

        _loadArc = new LvArc(card);
        _loadArc.SetSize(diameter, diameter);
        _loadArc.Align(LvAlign.BottomMid, 0, -4);
        _loadArc.SetRange(0, 100);
        _loadArc.Value = 0;
        _loadArc.IsReadOnly = true;
        _loadArc.SetArcColor(LoadColor, LvPart.Indicator);
        _loadArc.SetArcColor(Background, LvPart.Main);
        _loadArc.SetBackgroundOpacity(0, LvPart.Knob);

        _loadValue = new LvLabel(_loadArc, "0%");
        _loadValue.Center();
        _loadValue.SetTextColor(Text);
        _loadValue.SetFontSize(28);
    }

    private void BuildTemperatureCard(LvObject parent, int x, int y, int width, int height)
    {
        var card = Card(parent, "SOC TEMPERATURE", x, y, width, height);
        var diameter = Math.Min(width - 40, height - 60);

        _tempArc = new LvArc(card);
        _tempArc.SetSize(diameter, diameter);
        _tempArc.Align(LvAlign.BottomMid, 0, -4);
        _tempArc.SetRange(20, 90);
        _tempArc.Value = 20;
        _tempArc.IsReadOnly = true;
        _tempArc.SetArcColor(TempColor, LvPart.Indicator);
        _tempArc.SetArcColor(Background, LvPart.Main);
        _tempArc.SetBackgroundOpacity(0, LvPart.Knob);

        _tempValue = new LvLabel(_tempArc, "--");
        _tempValue.Center();
        _tempValue.SetTextColor(Text);
        _tempValue.SetFontSize(28);
    }

    private void BuildMemoryCard(LvObject parent, int x, int y, int width, int height)
    {
        var card = Card(parent, "MEMORY", x, y, width, height);

        _memoryValue = new LvLabel(card, "--");
        _memoryValue.Align(LvAlign.LeftMid, 0, -10);
        _memoryValue.SetTextColor(Text);
        _memoryValue.SetFontSize(28);

        _memoryBar = new LvBar(card);
        _memoryBar.SetSize(width - 44, 12);
        _memoryBar.Align(LvAlign.BottomLeft, 0, -16);
        _memoryBar.SetRange(0, 100);
        _memoryBar.SetValue(0, animate: false);
        _memoryBar.SetBackgroundColor(Background);
        _memoryBar.SetBackgroundColor(MemoryColor, LvPart.Indicator);
        _memoryBar.SetRadius(6);
        _memoryBar.SetRadius(6, LvPart.Indicator);
    }

    private void BuildChart(LvObject parent, int x, int y, int width, int height)
    {
        var card = Card(parent, "LOAD % / TEMPERATURE C", x, y, width, height);

        _chart = new LvChart(card);
        _chart.SetSize(width - 44, height - 44);
        _chart.Align(LvAlign.BottomMid, 0, -4);
        _chart.Type = LvChartType.Line;
        _chart.PointCount = ChartPoints;

        // Shift mode turns the chart into a rolling window: the oldest sample drops off the left
        // as each new one arrives, which is what a telemetry trace should do.
        _chart.UpdateMode = LvChartUpdateMode.Shift;
        _chart.SetRange(LvChartAxis.PrimaryY, 0, 100);
        _chart.SetDivisionLines(5, 9);
        _chart.SetBackgroundColor(Background);
        _chart.SetBorderWidth(0);
        _chart.SetRadius(8);
        _chart.SetLineColor(LvColor.FromRgb(0x243244u), LvPart.Items);

        _loadSeries = _chart.AddSeries(LoadColor);
        _tempSeries = _chart.AddSeries(TempColor);

        _loadSeries.Fill(0);
        _tempSeries.Fill(0);
    }

    /// <summary>
    /// Pushes a new sample into the UI. Must run on the LVGL thread - the sampler posts it there
    /// through <see cref="LvglApplication.Post"/>.
    /// </summary>
    public void Update(in SensorReading reading)
    {
        var load = (int)Math.Round(reading.CpuLoadPercent);
        _loadArc.Value = load;
        _loadValue.Text = string.Create(CultureInfo.InvariantCulture, $"{load}%");

        if (!double.IsNaN(reading.CpuTemperatureC))
        {
            _tempArc.Value = (int)Math.Round(reading.CpuTemperatureC);
            _tempValue.Text = string.Create(CultureInfo.InvariantCulture, $"{reading.CpuTemperatureC:F1}C");
            _tempValue.SetTextColor(reading.CpuTemperatureC >= 75 ? TempColor : Text);
        }

        var memory = (int)Math.Round(reading.MemoryUsedPercent);
        _memoryBar.SetValue(memory, animate: false);
        _memoryValue.Text = reading.MemoryTotalMb > 0
            ? string.Create(CultureInfo.InvariantCulture,
                $"{memory}% of {reading.MemoryTotalMb / 1024.0:F1} GiB")
            : string.Create(CultureInfo.InvariantCulture, $"{memory}%");

        _uptime.Text = string.Create(CultureInfo.InvariantCulture,
            $"up {(int)reading.Uptime.TotalHours:D2}:{reading.Uptime.Minutes:D2}:{reading.Uptime.Seconds:D2}");

        _loadSeries.AddPoint(load);
        _tempSeries.AddPoint(double.IsNaN(reading.CpuTemperatureC) ? 0 : (int)Math.Round(reading.CpuTemperatureC));
    }

    /// <summary>Shows a message in the header, used to report a sampling failure.</summary>
    public void SetStatus(string message) => _status.Text = message;
}
