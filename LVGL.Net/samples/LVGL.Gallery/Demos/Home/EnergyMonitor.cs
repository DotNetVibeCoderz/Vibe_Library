using System.Globalization;
using Lvgl.Drawing;
using Lvgl.Gallery.Shell;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Home;

/// <summary>
/// A household energy monitor: solar production against consumption on a live chart, the home
/// battery's charge, the power flowing to or from the grid on a gauge, and today's totals. The
/// readings are simulated one second at a time.
/// </summary>
internal sealed class EnergyMonitor : Demo
{
    private const int History = 90;

    private static readonly LvColor Solar = LvColor.FromRgb(0xE0A019u);

    private readonly Random _random = new(42);
    private LvChartSeries _solarSeries = null!, _loadSeries = null!;
    private LvArc _gauge = null!;
    private LvLabel _gridValue = null!, _gridDirection = null!, _batteryValue = null!, _solarNow = null!, _loadNow = null!;
    private LvBar _battery = null!;
    private LvTable _totals = null!;
    private double _clock = 11.5;   // hours of the simulated day
    private double _batteryCharge = 0.62;
    private double _solarKwh = 6.8, _loadKwh = 7.9, _importKwh = 2.1, _exportKwh = 1.0;

    public override Category Category => Category.Home;
    public override string Title => "Energy monitor";
    public override string Summary => "Solar against consumption on a live chart, battery charge, grid flow on a gauge and today's totals in a table.";
    public override string BuiltWith => "LvChart  LvArc  LvScale  LvTable  LvBar";

    protected override void Build(LvObject stage, int width, int height)
    {
        var chartWidth = width - 260;

        // Live chart: production and consumption, last 90 seconds.
        var card = Card(stage, 0, 0, chartWidth, height - 128);
        Caption(card, "Power, last 90 s").SetPosition(18, 14);
        _solarNow = Legend(card, Solar, chartWidth - 250);
        _loadNow = Legend(card, Accent, chartWidth - 128);

        var chart = new LvChart(card);
        chart.SetSize(chartWidth - 36, height - 128 - 60);
        chart.SetPosition(18, 44);
        chart.Type = LvChartType.Line;
        chart.PointCount = History;
        chart.UpdateMode = LvChartUpdateMode.Shift;
        chart.SetRange(LvChartAxis.PrimaryY, 0, 6000);
        chart.SetDivisionLines(4, 0);
        chart.SetBackgroundOpacity(0);
        chart.SetBorderWidth(0);
        chart.SetPadding(0);
        chart.SetLineColor(Palette.Sunken);
        chart.SetLineWidth(3, LvPart.Items);
        chart.SetPartSize(0, 0, LvPart.Indicator);
        _solarSeries = chart.AddSeries(Solar);
        _loadSeries = chart.AddSeries(Accent);

        // Battery under the chart.
        var battery = Card(stage, 0, height - 116, chartWidth, 116);
        Caption(battery, "Home battery").SetPosition(18, 14);
        _batteryValue = Text(battery, "", 34, Face.Display, Palette.Ink);
        _batteryValue.Align(LvAlign.TopRight, -18, 6);
        _battery = new LvBar(battery);
        _battery.SetSize(chartWidth - 36, 22);
        _battery.Align(LvAlign.BottomLeft, 18, -18);
        _battery.SetRange(0, 1000);
        _battery.SetRadius(6);
        _battery.SetRadius(6, LvPart.Indicator);
        _battery.SetBackgroundColor(Palette.Sunken);
        _battery.SetBackgroundColor(Palette.Good, LvPart.Indicator);

        // Grid gauge: export to the left of centre, import to the right.
        var side = Bare(new LvPanel(stage, 244, height));
        side.SetPosition(width - 244, 0);
        var gaugeCard = Card(side, 0, 0, 244, 236);
        Caption(gaugeCard, "Grid").SetPosition(18, 14);

        var scale = new LvScale(gaugeCard);
        scale.SetSize(176, 176);
        scale.Align(LvAlign.TopMid, 0, 40);
        scale.Mode = LvScaleMode.RoundInner;
        scale.SetRange(-5, 5);
        scale.TotalTickCount = 11;
        scale.MajorTickEvery = 5;
        scale.ShowLabels = true;
        scale.AngleRange = 240;
        scale.Rotation = 150;
        scale.SetFont(Fonts[Face.Mono, 10]);
        scale.SetTextColor(Palette.InkMuted);
        scale.SetLineColor(Palette.Rule, LvPart.Items);
        scale.SetLineColor(Palette.InkMuted, LvPart.Indicator);

        _gauge = new LvArc(gaugeCard);
        _gauge.SetSize(204, 204);
        _gauge.Align(LvAlign.TopMid, 0, 26);
        _gauge.Rotation = 150;
        _gauge.SetBackgroundAngles(0, 240);
        _gauge.SetRange(-5000, 5000);
        _gauge.Mode = LvArcMode.Symmetrical;
        _gauge.IsClickable = false;
        _gauge.SetArcWidth(10);
        _gauge.SetArcWidth(10, LvPart.Indicator);
        _gauge.SetArcColor(Palette.Sunken);
        _gauge.SetBackgroundOpacity(0, LvPart.Knob);
        _gauge.SetPadding(0, LvPart.Knob);

        _gridValue = Text(gaugeCard, "", 30, Face.Display, Palette.Ink);
        _gridValue.Align(LvAlign.TopMid, 0, 116);
        _gridDirection = Text(gaugeCard, "", 11, Face.Mono, Palette.InkMuted);
        _gridDirection.Align(LvAlign.TopMid, 0, 154);

        // Today's totals.
        var totals = Card(side, 0, 248, 244, height - 248);
        Caption(totals, "Today").SetPosition(18, 14);
        _totals = new LvTable(totals);
        _totals.SetPosition(8, 34);
        _totals.ColumnCount = 2;
        _totals.SetColumnWidth(0, 130);
        _totals.SetColumnWidth(1, 96);
        _totals.SetBorderWidth(0);
        _totals.SetBackgroundOpacity(0);
        _totals.SetBackgroundOpacity(0, LvPart.Items);
        _totals.SetBorderWidth(0, LvPart.Items);
        _totals.SetPadding(5, LvPart.Items);
        _totals.SetFont(Fonts[Face.Body, 14], LvPart.Items);
        _totals.SetTextColor(Palette.Ink, LvPart.Items);
        _totals.IsScrollable = false;

        // Prime the chart with history so it is never empty.
        for (var i = 0; i < History; i++) Sample();
        Every(1000, Sample);
    }

    private LvLabel Legend(LvObject parent, LvColor colour, int x)
    {
        Box(parent, x, 19, 10, 10, colour, 5);
        var value = Text(parent, "", 14, Face.Body, Palette.Ink);
        value.SetPosition(x + 16, 13);
        return value;
    }

    private void Sample()
    {
        _clock += 1 / 60.0;   // one simulated minute per second

        // Solar follows the sun with passing clouds; the house has a base load plus appliances.
        var sun = Math.Max(0, Math.Sin((_clock - 6) / 13 * Math.PI));
        var cloud = 0.75 + 0.25 * Math.Sin(_clock * 9) * Math.Sin(_clock * 2.3);
        var solar = 4800 * sun * cloud + _random.Next(-60, 60);
        var load = 650 + 900 * Math.Max(0, Math.Sin(_clock * 3.1)) + (_random.Next(12) == 0 ? 1800 : 0) + _random.Next(-80, 80);
        solar = Math.Max(0, solar);

        // Surplus charges the battery first, then exports; a deficit drains it, then imports.
        var surplus = solar - load;
        var batteryPower = Math.Clamp(surplus, -2500, 2500);
        if (batteryPower > 0 && _batteryCharge >= 1) batteryPower = 0;
        if (batteryPower < 0 && _batteryCharge <= 0.1) batteryPower = 0;
        _batteryCharge = Math.Clamp(_batteryCharge + batteryPower / 3600.0 / 10_000 * 60, 0, 1);
        var grid = -(surplus - batteryPower);   // positive = importing

        var hours = 1 / 60.0;
        _solarKwh += solar / 1000 * hours;
        _loadKwh += load / 1000 * hours;
        if (grid > 0) _importKwh += grid / 1000 * hours; else _exportKwh += -grid / 1000 * hours;

        _solarSeries.AddPoint((int)solar);
        _loadSeries.AddPoint((int)load);
        _solarNow.Text = $"Solar  {solar / 1000:0.0} kW";
        _loadNow.Text = $"Home  {load / 1000:0.0} kW";

        _gauge.Value = (int)grid;
        _gauge.SetArcColor(grid > 0 ? Palette.Bad : Palette.Good, LvPart.Indicator);
        _gridValue.Text = $"{Math.Abs(grid) / 1000:0.00} kW";
        _gridDirection.Text = grid > 50 ? "IMPORTING" : grid < -50 ? "EXPORTING" : "BALANCED";
        _gridDirection.SetTextColor(grid > 50 ? Palette.Bad : grid < -50 ? Palette.Good : Palette.InkMuted);

        _battery.SetValue((int)(_batteryCharge * 1000), animate: true);
        _batteryValue.Text = _batteryCharge.ToString("0%", CultureInfo.InvariantCulture);

        _totals.SetRows(
        [
            ["Solar", $"{_solarKwh:0.0} kWh"],
            ["Used", $"{_loadKwh:0.0} kWh"],
            ["From grid", $"{_importKwh:0.0} kWh"],
            ["To grid", $"{_exportKwh:0.0} kWh"],
            ["Self-powered", $"{Math.Clamp(1 - _importKwh / Math.Max(_loadKwh, 0.1), 0, 1):0%}"],
        ]);
    }
}
