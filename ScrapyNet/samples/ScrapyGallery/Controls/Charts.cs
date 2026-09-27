using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ScrapyGallery.Infrastructure;

namespace ScrapyGallery.Controls;

/// <summary>The three IBM Plex families bundled with the gallery.</summary>
public static class Fonts
{
    public static readonly FontFamily DisplayFamily = new("avares://ScrapyGallery/Assets/Fonts#IBM Plex Sans Condensed");
    public static readonly FontFamily BodyFamily = new("avares://ScrapyGallery/Assets/Fonts#IBM Plex Sans");
    public static readonly FontFamily MonoFamily = new("avares://ScrapyGallery/Assets/Fonts#IBM Plex Mono");

    public static readonly Typeface Display = new(DisplayFamily, FontStyle.Normal, FontWeight.SemiBold);
    public static readonly Typeface Body = new(BodyFamily);
    public static readonly Typeface Mono = new(MonoFamily);
}

/// <summary>One named line for <see cref="LineChart"/>.</summary>
public sealed record ChartSeries(string Name, IReadOnlyList<SeriesPoint> Points, Color Color);

/// <summary>A minimal multi-series line chart: thread-coloured axes, labelled ends, no chart junk.</summary>
public sealed class LineChart : Control
{
    private IReadOnlyList<ChartSeries> _series = [];

    public string Title { get; set; } = "";

    public string XLabel { get; set; } = "";

    public void Update(IReadOnlyList<ChartSeries> series)
    {
        _series = series;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var b = Bounds;
        const double left = 44, right = 16, bottom = 28;
        // Series names get their own band above the plot so they never sit on a line.
        var top = 34 + 16 * Math.Max(1, _series.Count(s => s.Points.Count >= 2));
        var plot = new Rect(left, top, Math.Max(10, b.Width - left - right), Math.Max(10, b.Height - top - bottom));
        Text(context, Title, new Point(0, 2), 12.5, Palette.Ink, Fonts.Display);

        var axis = new Pen(Palette.Brush(Palette.Thread), 1);
        context.DrawLine(axis, plot.BottomLeft, plot.BottomRight);
        context.DrawLine(axis, plot.BottomLeft, plot.TopLeft);

        var points = _series.SelectMany(s => s.Points).ToList();
        if (points.Count < 2)
        {
            Text(context, "Run the demo to draw this chart", new Point(plot.X + 8, plot.Center.Y - 8), 12, Palette.InkSoft, Fonts.Body);
            return;
        }
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X), maxY = Math.Max(1e-9, points.Max(p => p.Y));
        if (maxX - minX < 1e-9) maxX = minX + 1;
        Point Map(SeriesPoint p) => new(plot.X + (p.X - minX) / (maxX - minX) * plot.Width, plot.Bottom - p.Y / maxY * plot.Height);

        Text(context, Format(maxY), new Point(0, plot.Y - 6), 10.5, Palette.InkSoft, Fonts.Mono);
        Text(context, "0", new Point(30, plot.Bottom - 7), 10.5, Palette.InkSoft, Fonts.Mono);
        Text(context, $"{Format(maxX)} {XLabel}", new Point(plot.Right - 90, plot.Bottom + 6), 10.5, Palette.InkSoft, Fonts.Mono);

        var labelY = 22.0;
        foreach (var s in _series)
        {
            if (s.Points.Count < 2) continue;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(Map(s.Points[0]), false);
                foreach (var p in s.Points.Skip(1)) g.LineTo(Map(p));
            }
            context.DrawGeometry(null, new Pen(Palette.Brush(s.Color), 2), geometry);
            var end = Map(s.Points[^1]);
            context.DrawEllipse(Palette.Brush(s.Color), null, end, 3, 3);
            Text(context, $"● {s.Name}  {Format(s.Points[^1].Y)}", new Point(plot.X, labelY), 11, s.Color, Fonts.Mono);
            labelY += 16;
        }
    }

    private static string Format(double v) => v >= 100 ? v.ToString("N0", CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture);

    internal static void Text(DrawingContext context, string text, Point at, double size, Color color, Typeface face)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, Palette.Brush(color));
        context.DrawText(ft, at);
    }
}

/// <summary>Response counts by HTTP status as horizontal bars, coloured by class.</summary>
public sealed class StatusBars : Control
{
    private IReadOnlyDictionary<int, long> _counts = new Dictionary<int, long>();

    public void Update(IReadOnlyDictionary<int, long> counts)
    {
        _counts = counts;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        LineChart.Text(context, "Responses by status", new Point(0, 2), 12.5, Palette.Ink, Fonts.Display);
        if (_counts.Count == 0)
        {
            LineChart.Text(context, "No responses yet", new Point(0, 32), 12, Palette.InkSoft, Fonts.Body);
            return;
        }
        var max = _counts.Values.Max();
        var y = 32.0;
        var barWidth = Math.Max(20, Bounds.Width - 110);
        foreach (var (status, count) in _counts.OrderBy(kv => kv.Key))
        {
            var color = Palette.ForStatus(status);
            LineChart.Text(context, status.ToString(CultureInfo.InvariantCulture), new Point(0, y), 12, Palette.Ink, Fonts.Mono);
            var w = Math.Max(2, count / (double)max * barWidth);
            context.FillRectangle(Palette.Brush(color), new Rect(40, y + 3, w, 11), 2);
            LineChart.Text(context, count.ToString("N0", CultureInfo.InvariantCulture), new Point(46 + w, y), 11.5, Palette.InkSoft, Fonts.Mono);
            y += 22;
            if (y > Bounds.Height - 14) break;
        }
    }
}
