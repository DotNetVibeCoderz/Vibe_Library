using Lvgl.Drawing;
using Lvgl.Widgets;

namespace Lvgl.Ui;

/// <summary>
/// Turns a <see cref="UiDocument"/> into live LVGL widgets.
/// </summary>
/// <remarks>
/// Used by the designer to render a true preview, and available at run time for applications that
/// prefer to ship layouts as data rather than generated code. Both paths apply properties in the
/// same order, so a preview and a running app look identical.
/// </remarks>
public sealed class UiBuilder
{
    private readonly Dictionary<string, LvObject> _named = new(StringComparer.Ordinal);
    private readonly Dictionary<UiNode, LvObject> _byNode = [];

    /// <summary>Widgets that carried a <see cref="UiNode.Name"/>, keyed by that name.</summary>
    public IReadOnlyDictionary<string, LvObject> NamedWidgets => _named;

    /// <summary>
    /// Every widget created by the last <see cref="Build"/>, keyed by the node it came from.
    /// The designer uses this to map a selection in the tree onto the live widget it drew.
    /// </summary>
    public IReadOnlyDictionary<UiNode, LvObject> WidgetsByNode => _byNode;

    /// <summary>Looks up a named widget produced by the last <see cref="Build"/>.</summary>
    public T? Find<T>(string name) where T : LvObject =>
        _named.TryGetValue(name, out var widget) ? widget as T : null;

    /// <summary>
    /// Creates every widget in <paramref name="document"/> under <paramref name="parent"/>.
    /// </summary>
    /// <param name="document">Layout to instantiate.</param>
    /// <param name="parent">Container to build into; <see langword="null"/> uses the active screen.</param>
    /// <returns>The container the widgets were added to.</returns>
    public LvObject Build(UiDocument document, LvObject? parent = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        LvglRuntime.EnsureUiThread();

        _named.Clear();
        _byNode.Clear();

        var root = parent ?? LvScreen.Active();

        if (TryParseColor(document.BackgroundColor, out var background))
        {
            root.SetBackgroundColor(background);
        }

        foreach (var node in document.Children) Create(node, root);

        return root;
    }

    private void Create(UiNode node, LvObject parent)
    {
        var widget = Instantiate(node, parent);

        ApplyCommon(node, widget);
        _byNode[node] = widget;

        if (!string.IsNullOrEmpty(node.Name))
        {
            widget.Name = node.Name;
            _named[node.Name] = widget;
        }

        foreach (var child in node.Children) Create(child, widget);
    }

    private static LvObject Instantiate(UiNode node, LvObject parent) => node.Type switch
    {
        UiWidgetType.Panel => new LvPanel(parent),
        UiWidgetType.Label => new LvLabel(parent) { Text = node.Text ?? string.Empty },
        UiWidgetType.Button => new LvButton(parent) { Text = node.Text ?? string.Empty },
        UiWidgetType.Slider => CreateSlider(node, parent),
        UiWidgetType.Bar => CreateBar(node, parent),
        UiWidgetType.Arc => CreateArc(node, parent),
        UiWidgetType.Switch => new LvSwitch(parent) { IsOn = node.Value is > 0 },
        UiWidgetType.Checkbox => new LvCheckbox(parent, node.Text ?? string.Empty) { IsChecked = node.Value is > 0 },
        UiWidgetType.Dropdown => new LvDropdown(parent) { Options = node.Options },
        UiWidgetType.Roller => new LvRoller(parent) { Options = node.Options },
        UiWidgetType.TextArea => new LvTextArea(parent) { Text = node.Text ?? string.Empty },
        UiWidgetType.Chart => CreateChart(node, parent),
        _ => throw new NotSupportedException($"Unknown widget type {node.Type}."),
    };

    private static LvSlider CreateSlider(UiNode node, LvObject parent)
    {
        var slider = new LvSlider(parent);
        if (node.Minimum is { } min && node.Maximum is { } max) slider.SetRange(min, max);
        if (node.Value is { } value) slider.SetValue(value, animate: false);
        return slider;
    }

    private static LvBar CreateBar(UiNode node, LvObject parent)
    {
        var bar = new LvBar(parent);
        if (node.Minimum is { } min && node.Maximum is { } max) bar.SetRange(min, max);
        if (node.Value is { } value) bar.SetValue(value, animate: false);
        return bar;
    }

    private static LvArc CreateArc(UiNode node, LvObject parent)
    {
        var arc = new LvArc(parent);
        if (node.Minimum is { } min && node.Maximum is { } max) arc.SetRange(min, max);
        if (node.Value is { } value) arc.Value = value;
        return arc;
    }

    private static LvChart CreateChart(UiNode node, LvObject parent)
    {
        var chart = new LvChart(parent);

        if (node.ChartType is { } type) chart.Type = type;
        if (node.PointCount is { } points and > 0) chart.PointCount = points;
        if (node.Minimum is { } min && node.Maximum is { } max) chart.SetRange(LvChartAxis.PrimaryY, min, max);

        foreach (var color in node.SeriesColors)
        {
            chart.AddSeries(TryParseColor(color, out var parsed) ? parsed : LvColor.Blue);
        }

        return chart;
    }

    private static void ApplyCommon(UiNode node, LvObject widget)
    {
        if (node.Width is { } width && node.Height is { } height) widget.SetSize(width, height);
        else if (node.Width is { } onlyWidth) widget.Width = onlyWidth;
        else if (node.Height is { } onlyHeight) widget.Height = onlyHeight;

        // Alignment and absolute position are alternatives: LVGL treats the align offset as the
        // position, so setting both would make the explicit coordinates meaningless.
        if (node.Align is { } align) widget.Align(align, node.X, node.Y);
        else widget.SetPosition(node.X, node.Y);

        if (TryParseColor(node.BackgroundColor, out var background)) widget.SetBackgroundColor(background);
        if (TryParseColor(node.TextColor, out var text)) widget.SetTextColor(text);
        if (TryParseColor(node.BorderColor, out var border)) widget.SetBorderColor(border);

        if (node.BorderWidth is { } borderWidth) widget.SetBorderWidth(borderWidth);
        if (node.Radius is { } radius) widget.SetRadius(radius);
        if (node.Padding is { } padding) widget.SetPadding(padding);
        if (node.FontSize is { } fontSize) widget.SetFontSize(fontSize);

        if (node.Hidden) widget.IsHidden = true;
    }

    private static bool TryParseColor(string? text, out LvColor color)
    {
        color = default;
        return !string.IsNullOrWhiteSpace(text) && LvColor.TryParse(text, out color);
    }
}
