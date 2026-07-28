using Lvgl.Drawing;
using Lvgl.Styling;
using Lvgl.Widgets;

namespace Lvgl.Samples.Desktop.Demos;

/// <summary>
/// Local style properties, shared <see cref="LvStyle"/> objects, and per-state styling.
/// </summary>
internal sealed class StyleDemo : DemoPage
{
    // Shared styles must outlive every widget that references them, so they are fields of the page
    // and disposed only in Teardown - after the container's children have been deleted.
    private LvStyle? _cardStyle;
    private LvStyle? _pillStyle;

    public override string Title => "Styling";

    public override string Description => "Local properties, shared styles and state selectors";

    public override void Build(LvObject container, LvglApplication application)
    {
        container.SetFlexFlow(LvFlexFlow.RowWrap);
        container.SetGap(12, 12);

        _cardStyle = new LvStyle()
            .BackgroundColor(Theme.Surface)
            .BorderWidth(0)
            .Radius(14)
            .Padding(16);

        _pillStyle = new LvStyle()
            .BackgroundColor(Theme.SurfaceAlt)
            .TextColor(Theme.Text)
            .Radius(999)
            .Padding(10);

        BuildSharedStyleCard(container);
        BuildGradientCard(container);
        BuildStateCard(container);
    }

    private void BuildSharedStyleCard(LvObject parent)
    {
        var card = new LvPanel(parent, 300, 190);
        card.AddStyle(_cardStyle!);

        var heading = new LvLabel(card, "One shared style, four widgets");
        heading.Align(LvAlign.TopLeft);
        heading.SetTextColor(Theme.TextMuted);
        heading.SetFontSize(12);

        for (var i = 0; i < 4; i++)
        {
            var pill = new LvLabel(card, $"tag {i + 1}");
            pill.AddStyle(_pillStyle!);
            pill.Align(LvAlign.TopLeft, (i % 2) * 130, 34 + (i / 2) * 52);
        }
    }

    private void BuildGradientCard(LvObject parent)
    {
        var card = new LvPanel(parent, 300, 190);
        card.AddStyle(_cardStyle!);

        var heading = new LvLabel(card, "Gradients");
        heading.Align(LvAlign.TopLeft);
        heading.SetTextColor(Theme.TextMuted);
        heading.SetFontSize(12);

        var swatches = new (string Label, LvColor From, LvColor To)[]
        {
            ("ocean", LvColor.FromRgb(0x0EA5E9u), LvColor.FromRgb(0x1E3A8Au)),
            ("ember", LvColor.FromRgb(0xF97316u), LvColor.FromRgb(0x7F1D1Du)),
            ("moss", LvColor.FromRgb(0x84CC16u), LvColor.FromRgb(0x14532Du)),
        };

        for (var i = 0; i < swatches.Length; i++)
        {
            var (label, from, to) = swatches[i];

            var swatch = new LvPanel(card, 82, 96);
            swatch.Align(LvAlign.TopLeft, i * 90, 34);
            swatch.SetBorderWidth(0);
            swatch.SetRadius(10);
            swatch.SetBackgroundColor(from);
            swatch.SetBackgroundGradientColor(to);

            var caption = new LvLabel(swatch, label);
            caption.Align(LvAlign.BottomMid);
            // Pick the text colour from the gradient's end so the caption stays readable.
            caption.SetTextColor(to.ContrastingText());
            caption.SetFontSize(12);
        }
    }

    private void BuildStateCard(LvObject parent)
    {
        var card = new LvPanel(parent, 300, 190);
        card.AddStyle(_cardStyle!);

        var heading = new LvLabel(card, "State selectors");
        heading.Align(LvAlign.TopLeft);
        heading.SetTextColor(Theme.TextMuted);
        heading.SetFontSize(12);

        var hint = new LvLabel(card, "press and hold the button");
        hint.Align(LvAlign.BottomLeft);
        hint.SetTextColor(Theme.TextMuted);
        hint.SetFontSize(12);

        var button = new LvButton(card, "Press me");
        button.SetSize(180, 56);
        button.Align(LvAlign.Center, 0, -6);
        button.SetRadius(10);
        button.SetBackgroundColor(Theme.Accent);
        button.SetBackgroundColor(Theme.Accent.Darken(0.35f), LvPart.Main, LvState.Pressed);
        button.SetTextColor(Theme.Background);
    }

    public override void Teardown()
    {
        // Safe here: DemoShell clears the container's children before calling Teardown, so nothing
        // references these styles any more.
        _cardStyle?.Dispose();
        _pillStyle?.Dispose();
        _cardStyle = null;
        _pillStyle = null;
    }
}
