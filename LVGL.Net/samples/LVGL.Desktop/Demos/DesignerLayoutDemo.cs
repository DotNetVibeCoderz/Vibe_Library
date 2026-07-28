using Lvgl.Ui;
using Lvgl.Widgets;

namespace Lvgl.Samples.Desktop.Demos;

/// <summary>
/// Loads a layout produced by the WPF designer and brings it to life.
/// </summary>
/// <remarks>
/// This is the "ship the layout as data" path: the same <see cref="UiDocument"/> the designer
/// edits is instantiated at run time by <see cref="UiBuilder"/>, and application code finds the
/// widgets it needs by the names given in the designer. The alternative - generating C# with
/// <see cref="CSharpUiGenerator"/> - produces exactly the same widget tree.
/// </remarks>
internal sealed class DesignerLayoutDemo : DemoPage
{
    private readonly UiBuilder _builder = new();
    private LvLabel? _status;
    private int _clicks;

    public override string Title => "Designer file";

    public override string Description => "A .lvgl.json layout instantiated at run time by UiBuilder";

    public override void Build(LvObject container, LvglApplication application)
    {
        var document = LoadDocument(Math.Max(240, container.Width - 32));

        _builder.Build(document, container);

        // Widgets are located by the Name given in the designer, which is also the field name the
        // code generator would emit.
        _status = _builder.Find<LvLabel>("StatusLabel");

        if (_builder.Find<LvButton>("ActionButton") is { } button)
        {
            button.Clicked += (_, _) =>
            {
                _clicks++;
                if (_status is { IsAlive: true }) _status.Text = $"Clicked {_clicks} time(s)";
            };
        }

        if (_builder.Find<LvSlider>("BrightnessSlider") is { } slider &&
            _builder.Find<LvBar>("BrightnessBar") is { } bar)
        {
            slider.ValueChanged += (_, _) => bar.SetValue(slider.Value, animate: false);
            bar.SetValue(slider.Value, animate: false);
        }
    }

    /// <summary>
    /// Builds the document in code so the sample is self-contained. A real application would call
    /// <c>UiJson.Load("Dashboard.lvgl.json")</c> instead - the resulting object is identical.
    /// </summary>
    private static UiDocument LoadDocument(int width) => new()
    {
        Name = "DesignerSample",
        Width = width,
        Height = 380,
        Children =
        [
            new UiNode
            {
                Type = UiWidgetType.Panel,
                Name = "Card",
                Width = width,
                Height = 220,
                BackgroundColor = Theme.Surface.ToString(),
                Radius = 12,
                BorderWidth = 0,
                Padding = 16,
                Children =
                [
                    new UiNode
                    {
                        Type = UiWidgetType.Label,
                        Name = "TitleLabel",
                        Text = "Loaded from a designer document",
                        Align = LvAlign.TopLeft,
                        TextColor = Theme.Text.ToString(),
                        FontSize = 20,
                    },
                    new UiNode
                    {
                        Type = UiWidgetType.Label,
                        Name = "StatusLabel",
                        Text = "no interaction yet",
                        Align = LvAlign.TopLeft,
                        Y = 34,
                        TextColor = Theme.TextMuted.ToString(),
                    },
                    new UiNode
                    {
                        Type = UiWidgetType.Button,
                        Name = "ActionButton",
                        Text = "Click me",
                        Align = LvAlign.BottomLeft,
                        Width = 160,
                        Height = 44,
                        Radius = 8,
                        BackgroundColor = Theme.Accent.ToString(),
                        TextColor = Theme.Background.ToString(),
                    },
                    new UiNode
                    {
                        Type = UiWidgetType.Slider,
                        Name = "BrightnessSlider",
                        Align = LvAlign.TopRight,
                        Y = 90,
                        Width = 220,
                        Height = 8,
                        Minimum = 0,
                        Maximum = 100,
                        Value = 55,
                    },
                    new UiNode
                    {
                        Type = UiWidgetType.Bar,
                        Name = "BrightnessBar",
                        Align = LvAlign.TopRight,
                        Y = 130,
                        Width = 220,
                        Height = 8,
                        Minimum = 0,
                        Maximum = 100,
                        Value = 55,
                    },
                ],
            },
        ],
    };

    public override void Teardown() => _status = null;
}
