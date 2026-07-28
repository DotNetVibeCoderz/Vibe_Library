using Lvgl.Drawing;
using Lvgl.Widgets;

namespace Lvgl.Samples.Desktop.Demos;

/// <summary>Flex layout, percentage sizing and content sizing.</summary>
internal sealed class LayoutDemo : DemoPage
{
    public override string Title => "Layout";

    public override string Description => "Flex containers, percentage widths and content sizing";

    public override void Build(LvObject container, LvglApplication application)
    {
        container.SetFlexFlow(LvFlexFlow.Column);
        container.SetGap(12, 0);

        BuildRow(container, "Row, space between", LvFlexFlow.Row, LvFlexAlign.SpaceBetween);
        BuildRow(container, "Row, centered", LvFlexFlow.Row, LvFlexAlign.Center);
        BuildRow(container, "Row, space evenly", LvFlexFlow.Row, LvFlexAlign.SpaceEvenly);
        BuildPercentRow(container);
    }

    private static void BuildRow(LvObject parent, string title, LvFlexFlow flow, LvFlexAlign main)
    {
        var caption = new LvLabel(parent, title);
        caption.SetTextColor(Theme.TextMuted);
        caption.SetFontSize(12);

        var row = new LvPanel(parent);
        row.SetSize(LvCoord.Percent(100), 64);
        row.SetBackgroundColor(Theme.Surface);
        row.SetBorderWidth(0);
        row.SetRadius(10);
        row.SetPadding(10);
        row.SetFlexFlow(flow);
        row.SetFlexAlign(main, LvFlexAlign.Center, LvFlexAlign.Center);
        row.IsScrollable = false;

        foreach (var label in (string[])["alpha", "beta", "gamma"])
        {
            var chip = new LvPanel(row);

            // LV_SIZE_CONTENT: the chip shrinks to fit its label instead of taking a fixed width.
            chip.SetSize(LvCoord.SizeContent, LvCoord.SizeContent);
            chip.SetBackgroundColor(Theme.SurfaceAlt);
            chip.SetBorderWidth(0);
            chip.SetRadius(999);
            chip.SetPadding(10);

            var text = new LvLabel(chip, label);
            text.SetTextColor(Theme.Text);
        }
    }

    private static void BuildPercentRow(LvObject parent)
    {
        var caption = new LvLabel(parent, "Percentage widths (25 / 50 / 25)");
        caption.SetTextColor(Theme.TextMuted);
        caption.SetFontSize(12);

        var row = new LvPanel(parent);
        row.SetSize(LvCoord.Percent(100), 64);
        row.SetBackgroundColor(Theme.Surface);
        row.SetBorderWidth(0);
        row.SetRadius(10);
        row.SetPadding(6);
        row.SetFlexFlow(LvFlexFlow.Row);
        row.SetGap(0, 6);
        row.IsScrollable = false;

        foreach (var (percent, color) in ((int Percent, Drawing.LvColor Color)[])
                 [(25, Theme.Accent), (50, Theme.Success), (25, Theme.AccentWarm)])
        {
            var cell = new LvPanel(row);

            // Percentages are encoded in the coordinate itself, so they must be produced by
            // LvCoord.Percent and never combined with ordinary arithmetic.
            cell.SetSize(LvCoord.Percent(percent - 2), LvCoord.Percent(100));
            cell.SetBackgroundColor(color);
            cell.SetBorderWidth(0);
            cell.SetRadius(8);

            var text = new LvLabel(cell, $"{percent}%");
            text.Center();
            text.SetTextColor(color.ContrastingText());
        }
    }
}
