using System.Globalization;
using Lvgl.Gallery.Shell;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Work;

/// <summary>
/// A year of sales, aggregated by SQL: monthly revenue as bars, filterable by region, with the
/// regions ranked beside it. Pick a region and the chart and figures re-query.
/// </summary>
internal sealed class SalesReport : Demo
{
    private GalleryDatabase _db = null!;
    private LvChart _chart = null!;
    private LvChartSeries _series = null!;
    private LvLabel _total = null!, _best = null!, _change = null!;
    private readonly List<LvLabel> _monthLabels = [];
    private readonly List<LvButton> _filters = [];
    private readonly List<(LvBar Bar, LvLabel Name, LvLabel Amount)> _regions = [];
    private string? _region;

    public override Category Category => Category.Work;
    public override string Title => "Sales report";
    public override string Summary => "Twelve months of sales grouped in SQL, drawn as bars. Filter by region and every figure re-queries.";
    public override string BuiltWith => "LvChart  LvBar  LvButton  SQLite";

    protected override void Build(LvObject stage, int width, int height)
    {
        _db = Context.Database;
        var chartWidth = width - 250;

        // Region filter.
        var filters = Bare(new LvPanel(stage, chartWidth, 36));
        filters.SetFlexFlow(LvFlexFlow.Row);
        filters.SetGap(0, 8);
        foreach (var region in new string?[] { null }.Concat(GalleryDatabase.Regions))
        {
            var button = Button(filters, region ?? "All regions", region is null ? 112 : 78, 34, Palette.Card, Palette.Ink, 14);
            button.SetBorderColor(Palette.Rule);
            var chosen = region;
            button.Clicked += (_, _) => { _region = chosen; Refresh(); };
            _filters.Add(button);
        }

        // Monthly revenue.
        var card = Card(stage, 0, 48, chartWidth, height - 48);
        Caption(card, "Revenue by month").SetPosition(18, 14);

        _chart = new LvChart(card);
        _chart.SetSize(chartWidth - 36, height - 48 - 84);
        _chart.SetPosition(18, 40);
        _chart.Type = LvChartType.Bar;
        _chart.PointCount = 12;
        _chart.SetDivisionLines(4, 0);
        _chart.SetBackgroundOpacity(0);
        _chart.SetBorderWidth(0);
        _chart.SetPadding(0);
        _chart.SetLineColor(Palette.Sunken);
        _chart.SetRadius(4, LvPart.Items);
        _chart.SetGap(0, 10, LvPart.Main);
        _series = _chart.AddSeries(Accent);

        var slot = (chartWidth - 36) / 12;
        for (var i = 0; i < 12; i++)
        {
            var month = Text(card, "", 11, Face.Mono, Palette.InkMuted);
            month.Width = slot;
            month.SetTextAlign(LvTextAlign.Center);
            month.SetPosition(18 + i * slot, height - 48 - 36);
            _monthLabels.Add(month);
        }

        // Figures and the region ranking.
        var side = Bare(new LvPanel(stage, 230, height));
        side.SetPosition(width - 230, 0);

        _total = Figure(side, 0, "Revenue, 12 months");
        _best = Figure(side, 74, "Best month");
        _change = Figure(side, 148, "Last month vs average");

        Caption(side, "By region").SetPosition(0, 232);
        for (var i = 0; i < GalleryDatabase.Regions.Length; i++)
        {
            var y = 258 + i * 42;
            var name = Text(side, "", 14, Face.Body, Palette.Ink);
            name.SetPosition(0, y);
            var amount = Text(side, "", 12, Face.Mono, Palette.InkMuted);
            amount.Align(LvAlign.TopRight, 0, y + 2);
            var bar = new LvBar(side);
            bar.SetSize(230, 6);
            bar.SetPosition(0, y + 24);
            bar.SetBackgroundColor(Palette.Sunken);
            bar.SetBackgroundColor(Accent, LvPart.Indicator);
            _regions.Add((bar, name, amount));
        }

        Refresh();
    }

    private LvLabel Figure(LvObject parent, int y, string caption)
    {
        Caption(parent, caption).SetPosition(0, y);
        var value = Text(parent, "", 32, Face.Display, Palette.Ink);
        value.SetPosition(0, y + 18);
        return value;
    }

    private void Refresh()
    {
        for (var i = 0; i < _filters.Count; i++)
        {
            var on = (i == 0 && _region is null) || (i > 0 && GalleryDatabase.Regions[i - 1] == _region);
            _filters[i].SetBackgroundColor(on ? Accent : Palette.Card);
            _filters[i].SetTextColor(on ? Palette.White : Palette.Ink);
            _filters[i].SetBorderWidth(on ? 0 : 1);
        }

        var months = _db.SalesByMonth(_region).TakeLast(12).ToList();
        var peak = months.Max(m => m.Amount);
        _chart.SetRange(LvChartAxis.PrimaryY, 0, (int)(peak * 1.15m));
        foreach (var month in months) _series.AddPoint((int)month.Amount);
        _chart.Refresh();

        for (var i = 0; i < _monthLabels.Count && i < months.Count; i++)
        {
            var date = DateTime.ParseExact(months[i].Label, "yyyy-MM", CultureInfo.InvariantCulture);
            _monthLabels[i].Text = date.ToString("MMM", CultureInfo.InvariantCulture);
        }

        var total = months.Sum(m => m.Amount);
        var best = months.MaxBy(m => m.Amount)!;
        var average = total / months.Count;
        var change = (months[^1].Amount - average) / average;

        _total.Text = Money(total);
        _best.Text = $"{DateTime.ParseExact(best.Label, "yyyy-MM", CultureInfo.InvariantCulture):MMMM}";
        _change.Text = change.ToString("+0.0%;-0.0%", CultureInfo.InvariantCulture);
        _change.SetTextColor(change >= 0 ? Palette.Good : Palette.Bad);

        var regions = _db.SalesByRegion();
        var top = regions.Max(r => r.Amount);
        for (var i = 0; i < _regions.Count && i < regions.Count; i++)
        {
            var (bar, name, amount) = _regions[i];
            var selected = _region is null || regions[i].Label == _region;
            name.Text = regions[i].Label;
            name.SetTextColor(selected ? Palette.Ink : Palette.InkFaint);
            amount.Text = Money(regions[i].Amount);
            bar.SetRange(0, (int)top);
            bar.SetValue((int)regions[i].Amount, animate: true);
            bar.SetBackgroundColor(selected ? Accent : Palette.Rule, LvPart.Indicator);
        }
    }

    private static string Money(decimal amount) => amount >= 1000
        ? (amount / 1000).ToString("$#,0.0k", CultureInfo.InvariantCulture)
        : amount.ToString("$0", CultureInfo.InvariantCulture);
}
