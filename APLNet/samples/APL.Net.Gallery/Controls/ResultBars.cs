// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AplNet.Gallery.Infrastructure;
using AplNet.Gallery.ViewModels;

namespace AplNet.Gallery.Controls;

/// <summary>
/// One horizontal bar per variant, length proportional to its median time - shorter is faster. Bars
/// grow into place as each variant finishes timing, which is the app's one deliberate animation: it
/// shows the measurement arriving rather than decorating it.
/// </summary>
public sealed class ResultBars : Control
{
    public static readonly StyledProperty<IReadOnlyList<Measurement>?> ItemsProperty =
        AvaloniaProperty.Register<ResultBars, IReadOnlyList<Measurement>?>(nameof(Items));

    private const double RowHeight = 34;
    private const double LabelWidth = 210;
    private const double ValueWidth = 170;
    private const double AllocWidth = 90;

    private readonly Dictionary<string, double> _shown = [];
    private readonly DispatcherTimer _timer;

    static ResultBars()
    {
        AffectsMeasure<ResultBars>(ItemsProperty);
    }

    public ResultBars()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Step());
    }

    public IReadOnlyList<Measurement>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty)
        {
            if (Items is null || Items.Count == 0)
                _shown.Clear();
            _timer.Start();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(availableSize.Width, 900), Math.Max(1, Items?.Count ?? 0) * RowHeight);

    private void Step()
    {
        if (Items is null)
        {
            _timer.Stop();
            return;
        }

        bool moving = false;
        foreach (Measurement m in Items)
        {
            double current = _shown.GetValueOrDefault(m.Name);
            double next = current + (1 - current) * 0.18;
            if (1 - next < 0.002)
                next = 1;
            else
                moving = true;
            _shown[m.Name] = next;
        }

        InvalidateVisual();
        if (!moving)
            _timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        IReadOnlyList<Measurement>? items = Items;
        if (items is null || items.Count == 0)
            return;

        double max = items.Max(m => m.MedianMs);
        double tpl = items.FirstOrDefault(m => m.Kind == VariantKind.Tpl)?.MedianMs ?? 0;
        double barSpace = Math.Max(40, Bounds.Width - LabelWidth - ValueWidth - AllocWidth);
        var label = new Typeface(Palette.Body, FontStyle.Normal, FontWeight.Medium);
        var mono = new Typeface(Palette.Mono);

        for (int i = 0; i < items.Count; i++)
        {
            Measurement m = items[i];
            double y = i * RowHeight;
            double progress = _shown.GetValueOrDefault(m.Name);
            Color color = Palette.For(m.Kind);

            DrawText(context, m.Name, label, 13, Palette.Ink, new Point(0, y + 8));

            double width = Math.Max(2, barSpace * (m.MedianMs / max) * progress);
            var bar = new Rect(LabelWidth, y + 7, width, RowHeight - 14);
            context.FillRectangle(new SolidColorBrush(color), bar, 3);

            string value = CaseViewModel.Format(m.MedianMs);
            string ratio = tpl > 0 && m.Kind != VariantKind.Tpl ? "  " + Ratio(tpl / m.MedianMs) : string.Empty;
            DrawText(context, value + ratio, mono, 12, m.Kind == VariantKind.Apl ? Palette.Apl : Palette.Muted, new Point(LabelWidth + width + 10, y + 9));

            if (m.BytesPerRun >= 0)
            {
                string bytes = FormatBytes(m.BytesPerRun);
                var formatted = new FormattedText(bytes, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, mono, 10, new SolidColorBrush(Palette.Muted));
                context.DrawText(formatted, new Point(Bounds.Width - formatted.Width, y + 10));
            }
        }
    }

    private static string Ratio(double speedup) =>
        speedup >= 1 ? $"{speedup:0.0}× ▲" : $"{1 / speedup:0.0}× ▼";

    private static string FormatBytes(long bytes) =>
        bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0.0} MB alloc" : bytes >= 1024 ? $"{bytes / 1024.0:0.0} KB alloc" : $"{bytes} B alloc";

    private static void DrawText(DrawingContext context, string text, Typeface typeface, double size, Color color, Point origin)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, new SolidColorBrush(color));
        context.DrawText(formatted, origin);
    }
}
