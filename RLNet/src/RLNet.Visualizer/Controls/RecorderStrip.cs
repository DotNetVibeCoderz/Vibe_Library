// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RLNet.Visualizer.Controls;

/// <summary>
/// One trace of a strip-chart recorder: a single series drawn against episode number, with its
/// current value read out at the right edge.
/// </summary>
/// <remarks>
/// <para>
/// Stacked, these are the console's signature. Three quantities — return, loss, exploration —
/// share one horizontal axis and sit flush against each other separated by hairlines, so the
/// stack reads as one continuous recorder trace rather than three charts. That layout is not
/// decoration: the whole story of a training run is the <em>relationship</em> between those
/// three over time, and boxing them into separate cards is what makes that relationship hard
/// to see.
/// </para>
/// <para>
/// Drawn directly in <see cref="Render"/> rather than built from shape controls. At 400 points
/// per trace redrawn every frame, a control tree would mean thousands of visuals created and
/// discarded a second; one geometry costs nothing.
/// </para>
/// </remarks>
public sealed class RecorderStrip : Control
{
    public static readonly StyledProperty<TraceBuffer?> TraceProperty =
        AvaloniaProperty.Register<RecorderStrip, TraceBuffer?>(nameof(Trace));

    public static readonly StyledProperty<string> LegendProperty =
        AvaloniaProperty.Register<RecorderStrip, string>(nameof(Legend), "TRACE");

    public static readonly StyledProperty<IBrush> TraceBrushProperty =
        AvaloniaProperty.Register<RecorderStrip, IBrush>(nameof(TraceBrush), Brushes.White);

    public static readonly StyledProperty<string> ValueFormatProperty =
        AvaloniaProperty.Register<RecorderStrip, string>(nameof(ValueFormat), "F1");

    /// <summary>Draw a baseline at zero. Meaningful for return, not for a loss that cannot go negative.</summary>
    public static readonly StyledProperty<bool> ShowZeroLineProperty =
        AvaloniaProperty.Register<RecorderStrip, bool>(nameof(ShowZeroLine));

    /// <summary>
    /// Overlay a moving average, drawing the raw series faintly behind it.
    /// </summary>
    /// <remarks>
    /// Per-episode return is genuinely noisy — CartPole can score 9 and then 400 under one
    /// unchanged policy — and a raw trace alone cannot answer the only question being asked of
    /// it, which is whether the agent is improving. The smoothed line answers that; the raw one
    /// stays visible behind it so the noise is not hidden, only demoted.
    /// </remarks>
    public static readonly StyledProperty<bool> ShowTrendProperty =
        AvaloniaProperty.Register<RecorderStrip, bool>(nameof(ShowTrend));

    static RecorderStrip()
    {
        AffectsRender<RecorderStrip>(
            TraceProperty, LegendProperty, TraceBrushProperty, ValueFormatProperty,
            ShowZeroLineProperty, ShowTrendProperty);
    }

    public TraceBuffer? Trace
    {
        get => GetValue(TraceProperty);
        set => SetValue(TraceProperty, value);
    }

    public string Legend
    {
        get => GetValue(LegendProperty);
        set => SetValue(LegendProperty, value);
    }

    public IBrush TraceBrush
    {
        get => GetValue(TraceBrushProperty);
        set => SetValue(TraceBrushProperty, value);
    }

    public string ValueFormat
    {
        get => GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    public bool ShowZeroLine
    {
        get => GetValue(ShowZeroLineProperty);
        set => SetValue(ShowZeroLineProperty, value);
    }

    public bool ShowTrend
    {
        get => GetValue(ShowTrendProperty);
        set => SetValue(ShowTrendProperty, value);
    }

    private static readonly IBrush RuleBrush = new SolidColorBrush(Color.Parse("#253243"));
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.Parse("#7E92A8"));
    private static readonly IBrush InkBrush = new SolidColorBrush(Color.Parse("#DCE6F0"));

    private static readonly Typeface LabelTypeface = new(
        new FontFamily("Inter, Segoe UI Variable Text, Segoe UI, SF Pro Text, Ubuntu, sans-serif"),
        FontStyle.Normal, FontWeight.SemiBold);

    private static readonly Typeface ReadoutTypeface = new(
        new FontFamily("Cascadia Mono, Consolas, SF Mono, DejaVu Sans Mono, monospace"));

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 4 || bounds.Height <= 4) return;

        // The legend sits inside the plot rather than above it, so the traces stay flush against
        // each other and the stack keeps reading as one instrument. The right gutter is sized for
        // the widest readout the traces actually produce - Pendulum's return reaches -1955.6, and
        // a narrower gutter clips it.
        const double padLeft = 10, padRight = 92, padTop = 16, padBottom = 8;

        var plot = new Rect(
            padLeft, padTop,
            Math.Max(1, bounds.Width - padLeft - padRight),
            Math.Max(1, bounds.Height - padTop - padBottom));

        DrawLegend(context, padLeft);

        var trace = Trace;
        if (trace is null || trace.Count < 2)
        {
            DrawPlaceholder(context, plot);
            return;
        }

        GetRange(trace, out float low, out float high);
        DrawZeroLine(context, plot, low, high);
        DrawTrace(context, plot, trace, low, high);
        DrawCurrentValue(context, bounds, plot, trace, low, high);
        DrawRangeLabels(context, bounds, low, high);
    }

    private void DrawLegend(DrawingContext context, double x)
    {
        var legend = new FormattedText(
            Legend, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            LabelTypeface, 9.5, DimBrush);

        context.DrawText(legend, new Point(x, 3));
    }

    private static void DrawPlaceholder(DrawingContext context, Rect plot)
    {
        // Before the first episode completes there is nothing to draw, and an empty panel reads
        // as broken. A midline plus a word says "waiting", which is the truth.
        var pen = new Pen(RuleBrush, 1, new DashStyle([3, 3], 0));
        double y = plot.Y + plot.Height / 2;
        context.DrawLine(pen, new Point(plot.X, y), new Point(plot.Right, y));

        var text = new FormattedText(
            "awaiting first episode", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelTypeface, 9.5, DimBrush);

        context.DrawText(text, new Point(plot.X + 4, y - text.Height - 3));
    }

    private static void GetRange(TraceBuffer trace, out float low, out float high)
    {
        low = trace.Minimum;
        high = trace.Maximum;

        // A flat trace has zero span and would divide by zero; pad it so the line sits mid-height
        // instead of collapsing onto an edge.
        float span = high - low;
        if (span < 1e-6f)
        {
            float pad = MathF.Max(1f, MathF.Abs(high) * 0.1f);
            low -= pad;
            high += pad;
        }
        else
        {
            // A little headroom so the extremes are not clipped by the frame.
            low -= span * 0.08f;
            high += span * 0.08f;
        }
    }

    private void DrawZeroLine(DrawingContext context, Rect plot, float low, float high)
    {
        if (!ShowZeroLine || low > 0f || high < 0f) return;

        double y = plot.Bottom - (0f - low) / (high - low) * plot.Height;
        context.DrawLine(new Pen(RuleBrush, 1), new Point(plot.X, y), new Point(plot.Right, y));
    }

    private void DrawTrace(DrawingContext context, Rect plot, TraceBuffer trace, float low, float high)
    {
        // With a trend line on top, the raw series drops to a faint hairline: it is context for
        // the smoothed line rather than the thing being read.
        double rawWidth = ShowTrend ? 0.8 : 1.4;
        double rawOpacity = ShowTrend ? 0.38 : 1.0;

        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            for (int i = 0; i < trace.Count; i++)
            {
                double x = plot.X + plot.Width * i / (double)Math.Max(1, trace.Count - 1);
                double y = plot.Bottom - (trace[i] - low) / (high - low) * plot.Height;
                var point = new Point(x, y);

                if (i == 0) sink.BeginFigure(point, isFilled: false);
                else sink.LineTo(point);
            }
            sink.EndFigure(false);
        }

        // A soft fill under the line gives the trace weight without a second colour. Rebuilt from
        // the same geometry closed to the baseline.
        var fill = new StreamGeometry();
        using (var sink = fill.Open())
        {
            sink.BeginFigure(new Point(plot.X, plot.Bottom), isFilled: true);
            for (int i = 0; i < trace.Count; i++)
            {
                double x = plot.X + plot.Width * i / (double)Math.Max(1, trace.Count - 1);
                double y = plot.Bottom - (trace[i] - low) / (high - low) * plot.Height;
                sink.LineTo(new Point(x, y));
            }
            sink.LineTo(new Point(plot.Right, plot.Bottom));
            sink.EndFigure(true);
        }

        if (TraceBrush is ISolidColorBrush solid)
        {
            var wash = new SolidColorBrush(solid.Color, ShowTrend ? 0.08 : 0.12);
            context.DrawGeometry(wash, null, fill);
        }

        var rawPen = TraceBrush is ISolidColorBrush raw
            ? new Pen(new SolidColorBrush(raw.Color, rawOpacity), rawWidth)
            : new Pen(TraceBrush, rawWidth);

        context.DrawGeometry(null, rawPen, geometry);

        if (ShowTrend) DrawTrend(context, plot, trace, low, high);
    }

    /// <summary>Draws a trailing moving average over the raw series.</summary>
    /// <remarks>
    /// The window is a fraction of what is on screen rather than a fixed episode count, so the
    /// line stays equally smooth whether the strip is showing 30 episodes or 400 — a fixed window
    /// is either too jumpy early or too sluggish late.
    /// </remarks>
    private void DrawTrend(DrawingContext context, Rect plot, TraceBuffer trace, float low, float high)
    {
        int window = Math.Clamp(trace.Count / 12, 3, 40);
        if (trace.Count < window * 2) return;

        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            bool started = false;
            float running = 0f;

            for (int i = 0; i < trace.Count; i++)
            {
                // A rolling sum rather than a fresh average per point: the naive version is
                // O(count x window) on every frame, which at 400 points and 60 Hz is real work
                // for a line nobody can see the difference in.
                running += trace[i];
                if (i >= window) running -= trace[i - window];
                if (i < window - 1) continue;

                double x = plot.X + plot.Width * i / (double)Math.Max(1, trace.Count - 1);
                double y = plot.Bottom - (running / window - low) / (high - low) * plot.Height;
                var point = new Point(x, y);

                if (!started) { sink.BeginFigure(point, isFilled: false); started = true; }
                else sink.LineTo(point);
            }

            if (started) sink.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(TraceBrush, 1.8, lineCap: PenLineCap.Round), geometry);
    }

    private void DrawCurrentValue(DrawingContext context, Rect bounds, Rect plot, TraceBuffer trace, float low, float high)
    {
        float current = trace[trace.Count - 1];

        double y = plot.Bottom - (current - low) / (high - low) * plot.Height;
        context.DrawEllipse(TraceBrush, null, new Point(plot.Right, y), 2.5, 2.5);

        var text = new FormattedText(
            current.ToString(ValueFormat, System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            ReadoutTypeface, 15, InkBrush);

        // Pinned to the right gutter rather than following the trace: a number that moves
        // vertically every frame is unreadable, which defeats the point of showing it.
        context.DrawText(text, new Point(bounds.Width - text.Width - 8, plot.Y + plot.Height / 2 - text.Height / 2));
    }

    private static void DrawRangeLabels(DrawingContext context, Rect bounds, float low, float high)
    {
        var highText = new FormattedText(
            high.ToString("G3", System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            ReadoutTypeface, 8.5, DimBrush);

        var lowText = new FormattedText(
            low.ToString("G3", System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            ReadoutTypeface, 8.5, DimBrush);

        context.DrawText(highText, new Point(bounds.Width - 86, 4));
        context.DrawText(lowText, new Point(bounds.Width - 86, bounds.Height - lowText.Height - 3));
    }
}
