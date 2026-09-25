// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AplNet.Gallery.Infrastructure;

namespace AplNet.Gallery.Controls;

/// <summary>
/// Small multiples of a profiler's thread view: for each traced variant, one lane per thread that did
/// work, spans filled where it was busy, all on one shared time axis. A variant finishes when its
/// longest lane does, so idle tails - the cost of a bad split - are visible at a glance.
/// </summary>
public sealed class LaneChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<LaneTrace>?> TracesProperty =
        AvaloniaProperty.Register<LaneChart, IReadOnlyList<LaneTrace>?>(nameof(Traces));

    private const double LaneHeight = 9;
    private const double LaneGap = 3;
    private const double TitleHeight = 26;
    private const double BlockGap = 18;
    private const double GutterWidth = 200;

    static LaneChart()
    {
        AffectsMeasure<LaneChart>(TracesProperty);
        AffectsRender<LaneChart>(TracesProperty);
    }

    public IReadOnlyList<LaneTrace>? Traces
    {
        get => GetValue(TracesProperty);
        set => SetValue(TracesProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0;
        foreach (LaneTrace trace in Traces ?? [])
            height += Math.Max(TitleHeight, trace.Lanes.Count * (LaneHeight + LaneGap)) + BlockGap + 8;
        return new Size(Math.Min(availableSize.Width, 1100), height);
    }

    public override void Render(DrawingContext context)
    {
        IReadOnlyList<LaneTrace>? traces = Traces;
        if (traces is null || traces.Count == 0)
            return;

        double total = traces.Max(t => t.TotalMs);
        double plot = Math.Max(50, Bounds.Width - GutterWidth - 80);
        var label = new Typeface(Palette.Body, FontStyle.Normal, FontWeight.SemiBold);
        var mono = new Typeface(Palette.Mono);
        var track = new SolidColorBrush(Color.Parse("#E6EAF0"));
        var finishPen = new Pen(new SolidColorBrush(Palette.Ink), 1.2, DashStyle.Dash);

        double y = 0;
        foreach (LaneTrace trace in traces)
        {
            var brush = new SolidColorBrush(Palette.For(trace.Kind));
            Text(context, trace.Name, label, 13, Palette.Ink, new Point(0, y + 4));
            Text(context, $"{trace.TotalMs:0.0} ms · {trace.Lanes.Count} threads", mono, 10, Palette.Muted, new Point(0, y + TitleHeight - 4));

            double laneTop = y + 8;
            for (int lane = 0; lane < trace.Lanes.Count; lane++)
            {
                double top = laneTop + lane * (LaneHeight + LaneGap);
                context.FillRectangle(track, new Rect(GutterWidth, top, plot, LaneHeight), 2);
                foreach ((double start, double end) in trace.Lanes[lane])
                {
                    double x0 = GutterWidth + plot * start / total;
                    double x1 = GutterWidth + plot * end / total;
                    context.FillRectangle(brush, new Rect(x0, top, Math.Max(1, x1 - x0), LaneHeight), 1.5f);
                }
            }

            double blockHeight = Math.Max(TitleHeight, trace.Lanes.Count * (LaneHeight + LaneGap));
            double finish = GutterWidth + plot * trace.TotalMs / total;
            context.DrawLine(finishPen, new Point(finish, laneTop - 3), new Point(finish, laneTop + blockHeight - LaneGap + 1));
            y += Math.Max(TitleHeight, trace.Lanes.Count * (LaneHeight + LaneGap)) + BlockGap + 8;
        }
    }

    private static void Text(DrawingContext context, string text, Typeface typeface, double size, Color color, Point origin) =>
        context.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, new SolidColorBrush(color)), origin);
}
