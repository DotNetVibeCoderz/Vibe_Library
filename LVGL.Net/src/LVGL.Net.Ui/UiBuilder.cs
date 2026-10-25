using Lvgl.Drawing;
using Lvgl.Styling;
using Lvgl.Widgets;

namespace Lvgl.Ui;

/// <summary>
/// Turns a <see cref="UiDocument"/> into live LVGL widgets.
/// </summary>
/// <remarks>
/// <para>
/// Used by the designer to render a true preview, and available at run time for applications that
/// prefer to ship layouts as data rather than generated code. Both paths apply properties in the
/// same order, so a preview and a running app look identical.
/// </para>
/// <para>
/// <b>Teardown.</b> The builder owns the shared <see cref="LvStyle"/>s and TrueType
/// <see cref="LvFont"/>s it creates, and the widgets point at them. Delete the widgets first
/// (clear the screen or delete the root), then <see cref="Dispose"/> the builder - the order every
/// LVGL style follows.
/// </para>
/// </remarks>
public sealed class UiBuilder : IDisposable
{
    private readonly Dictionary<string, LvObject> _named = new(StringComparer.Ordinal);
    private readonly Dictionary<UiNode, LvObject> _byNode = [];
    private readonly Dictionary<string, LvStyle> _styles = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Path, int Size), LvFont> _fonts = [];
    private readonly List<(LvKeyboard Keyboard, string Target)> _keyboards = [];
    private UiDocument? _document;

    /// <summary>
    /// Folder relative asset paths are resolved against - normally the folder the document was
    /// loaded from. Defaults to the current directory.
    /// </summary>
    public string AssetDirectory { get; set; } = Environment.CurrentDirectory;

    /// <summary>Widgets that carried a <see cref="UiNode.Name"/>, keyed by that name.</summary>
    public IReadOnlyDictionary<string, LvObject> NamedWidgets => _named;

    /// <summary>
    /// Every widget created by the last <see cref="Build"/>, keyed by the node it came from.
    /// The designer uses this to map a selection in the tree onto the live widget it drew.
    /// </summary>
    public IReadOnlyDictionary<UiNode, LvObject> WidgetsByNode => _byNode;

    /// <summary>The shared styles built for the document, keyed by style name.</summary>
    public IReadOnlyDictionary<string, LvStyle> Styles => _styles;

    /// <summary>
    /// Problems met while building that did not stop it - typically an asset file that is missing.
    /// The widget is still created, just without the asset.
    /// </summary>
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    private readonly List<string> _diagnostics = [];

    /// <summary>Looks up a named widget produced by the last <see cref="Build"/>.</summary>
    public T? Find<T>(string name) where T : LvObject =>
        _named.TryGetValue(name, out var widget) ? widget as T : null;

    /// <summary>
    /// Creates every widget in <paramref name="document"/> under <paramref name="parent"/>.
    /// </summary>
    /// <param name="document">Layout to instantiate.</param>
    /// <param name="parent">Container to build into; <see langword="null"/> uses the active screen.</param>
    /// <returns>The container the widgets were added to.</returns>
    /// <exception cref="InvalidOperationException">The builder was already used; create a new one per build.</exception>
    public LvObject Build(UiDocument document, LvObject? parent = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        LvglRuntime.EnsureUiThread();

        // A second build would replace styles the first build's widgets still point at.
        if (_document is not null) throw new InvalidOperationException("A UiBuilder builds once; create a new one for each build.");
        _document = document;

        var root = parent ?? LvScreen.Active();

        if (TryParseColor(document.BackgroundColor, out var background))
        {
            root.SetBackgroundColor(background);
        }

        foreach (var style in document.Styles) _styles[style.Name] = CreateStyle(style);

        foreach (var node in document.Children) Create(node, root);

        // Second pass: a keyboard may name a text area that appears later in the document.
        foreach (var (keyboard, target) in _keyboards)
        {
            if (_named.TryGetValue(target, out var widget) && widget is LvTextArea field) keyboard.TextArea = field;
        }

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

    private LvObject Instantiate(UiNode node, LvObject parent) => node.Type switch
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
        UiWidgetType.Image => CreateImage(node, parent),
        UiWidgetType.TabView => CreateTabView(node, parent),
        UiWidgetType.Tab => CreateTab(node, parent),
        UiWidgetType.List => CreateList(node, parent),
        UiWidgetType.TileView => new LvTileView(parent),
        UiWidgetType.Tile => CreateTile(node, parent),
        UiWidgetType.Keyboard => CreateKeyboard(node, parent),
        UiWidgetType.Spinner => new LvSpinner(parent),
        UiWidgetType.Led => CreateLed(node, parent),
        UiWidgetType.Scale => CreateScale(node, parent),
        UiWidgetType.Table => CreateTable(node, parent),
        UiWidgetType.Spinbox => CreateSpinbox(node, parent),
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

    private LvImage CreateImage(UiNode node, LvObject parent)
    {
        var image = new LvImage(parent);

        if (!string.IsNullOrEmpty(node.Symbol) && UiSymbols.IsKnown(node.Symbol))
        {
            image.SetSymbol(UiSymbols.Glyph(node.Symbol));
        }
        else if (ResolveAsset(node.Source) is { } path)
        {
            image.SetFile(path);
        }

        if (node.Rotation is { } rotation) image.Rotation = rotation;
        if (node.ImageScale is { } scale and > 0) image.Scale = scale;
        return image;
    }

    private static LvTabView CreateTabView(UiNode node, LvObject parent)
    {
        var tabs = new LvTabView(parent);
        if (node.TabBarPosition is { } position and (LvDir.Top or LvDir.Bottom or LvDir.Left or LvDir.Right))
        {
            tabs.TabBarPosition = position;
        }

        return tabs;
    }

    private static LvObject CreateTab(UiNode node, LvObject parent) => parent is LvTabView tabs
        ? tabs.AddTab(node.Text ?? node.Name ?? $"Tab {tabs.TabCount + 1}")
        : throw new InvalidOperationException($"A Tab must be inside a TabView, not a {parent.GetType().Name}.");

    private static LvObject CreateTile(UiNode node, LvObject parent) => parent is LvTileView view
        ? view.AddTile(node.Column ?? 0, node.Row ?? 0)
        : throw new InvalidOperationException($"A Tile must be inside a TileView, not a {parent.GetType().Name}.");

    private static LvList CreateList(UiNode node, LvObject parent)
    {
        var list = new LvList(parent);
        foreach (var (isHeading, text) in node.ListEntries())
        {
            if (isHeading) list.AddText(text);
            else list.AddButton(text);
        }

        return list;
    }

    private LvKeyboard CreateKeyboard(UiNode node, LvObject parent)
    {
        var keyboard = new LvKeyboard(parent);
        if (node.KeyboardMode is { } mode) keyboard.Mode = mode;
        if (!string.IsNullOrEmpty(node.Target)) _keyboards.Add((keyboard, node.Target));
        return keyboard;
    }

    private static LvLed CreateLed(UiNode node, LvObject parent)
    {
        var led = new LvLed(parent);
        if (TryParseColor(node.AccentColor, out var color)) led.Color = color;
        if (node.Value is { } brightness) led.Brightness = (byte)Math.Clamp(brightness, 0, 255);
        return led;
    }

    private static LvScale CreateScale(UiNode node, LvObject parent)
    {
        var scale = new LvScale(parent);
        var mode = node.ScaleMode ?? LvScaleMode.HorizontalBottom;
        scale.Mode = mode;

        // A round scale with LVGL's defaults draws a full circle starting at 3 o'clock, which is
        // never what a gauge wants; 270 degrees opening at the bottom is.
        if (mode is LvScaleMode.RoundInner or LvScaleMode.RoundOuter)
        {
            scale.AngleRange = 270;
            scale.Rotation = 135;
        }

        if (node.Minimum is { } min && node.Maximum is { } max) scale.SetRange(min, max);
        if (node.TickCount is { } ticks) scale.TotalTickCount = ticks;
        if (node.MajorTickEvery is { } every) scale.MajorTickEvery = every;
        scale.ShowLabels = true;
        return scale;
    }

    private static LvTable CreateTable(UiNode node, LvObject parent)
    {
        var table = new LvTable(parent);
        if (node.Options.Count > 0) table.SetRows(node.TableRows());
        return table;
    }

    private static LvSpinbox CreateSpinbox(UiNode node, LvObject parent)
    {
        var spinbox = new LvSpinbox(parent);
        if (node.Digits is { } digits) spinbox.SetDigitFormat(digits, node.DecimalPlaces ?? 0);
        else if (node.DecimalPlaces is { } places and > 0) spinbox.SetDigitFormat(Math.Max(5, places + 1), places);
        if (node.Minimum is { } min && node.Maximum is { } max) spinbox.SetRange(min, max);
        if (node.Value is { } value) spinbox.Value = value;
        return spinbox;
    }

    private void ApplyCommon(UiNode node, LvObject widget)
    {
        // Shared styles first, so the node's own properties - local styles in LVGL - win.
        foreach (var name in node.Styles)
        {
            if (_styles.TryGetValue(name, out var style)) widget.AddStyle(style);
        }

        // Tabs and tiles are sized and placed by their host; geometry on them would fight it.
        if (node.Type is not (UiWidgetType.Tab or UiWidgetType.Tile))
        {
            if (node.Width is { } width && node.Height is { } height) widget.SetSize(width, height);
            else if (node.Width is { } onlyWidth) widget.Width = onlyWidth;
            else if (node.Height is { } onlyHeight) widget.Height = onlyHeight;

            // Alignment and absolute position are alternatives: LVGL treats the align offset as the
            // position, so setting both would make the explicit coordinates meaningless.
            if (node.Align is { } align) widget.Align(align, node.X, node.Y);
            else widget.SetPosition(node.X, node.Y);
        }

        if (TryParseColor(node.BackgroundColor, out var background)) widget.SetBackgroundColor(background);
        if (TryParseColor(node.TextColor, out var text)) widget.SetTextColor(text);
        if (TryParseColor(node.BorderColor, out var border)) widget.SetBorderColor(border);
        if (node.Type != UiWidgetType.Led && TryParseColor(node.AccentColor, out var accent)) ApplyAccent(node.Type, widget, accent);

        if (node.BorderWidth is { } borderWidth) widget.SetBorderWidth(borderWidth);
        if (node.Radius is { } radius) widget.SetRadius(radius);
        if (node.Padding is { } padding) widget.SetPadding(padding);

        if (FontFor(node.Font, node.FontSize) is { } font) widget.SetFont(font);
        else if (node.FontSize is { } fontSize) widget.SetFontSize(fontSize);

        if (node.Hidden) widget.IsHidden = true;
    }

    /// <summary>
    /// The accent colour lands on whichever part each widget draws its highlight with.
    /// </summary>
    private static void ApplyAccent(UiWidgetType type, LvObject widget, LvColor accent)
    {
        var (setter, part) = AccentPart(type);
        if (setter == nameof(LvObject.SetArcColor)) widget.SetArcColor(accent, part);
        else widget.SetBackgroundColor(accent, part);
    }

    /// <summary>
    /// How <see cref="UiNode.AccentColor"/> is applied to a widget kind: the setter and the part.
    /// Shared with <see cref="CSharpUiGenerator"/>, so a preview and generated code agree.
    /// </summary>
    internal static (string Setter, LvPart Part) AccentPart(UiWidgetType type) => type switch
    {
        UiWidgetType.Arc or UiWidgetType.Spinner => (nameof(LvObject.SetArcColor), LvPart.Indicator),
        _ => (nameof(LvObject.SetBackgroundColor), LvPart.Indicator),
    };

    private LvStyle CreateStyle(UiStyle definition)
    {
        var style = new LvStyle();

        if (TryParseColor(definition.BackgroundColor, out var background)) style.BackgroundColor(background);
        if (definition.BackgroundOpacity is { } opacity) style.BackgroundOpacity((byte)Math.Clamp(opacity, 0, 255));
        if (TryParseColor(definition.TextColor, out var text)) style.TextColor(text);
        if (TryParseColor(definition.BorderColor, out var border)) style.BorderColor(border);
        if (definition.BorderWidth is { } borderWidth) style.BorderWidth(borderWidth);
        if (definition.Radius is { } radius) style.Radius(radius);
        if (definition.Padding is { } padding) style.Padding(padding);

        if (FontFor(definition.Font, definition.FontSize) is { } font) style.Font(font);
        else if (definition.FontSize is { } fontSize) style.FontSize(fontSize);

        return style;
    }

    /// <summary>Default pixel size of a TrueType font whose node gives no FontSize.</summary>
    public const int DefaultTrueTypeSize = 16;

    private LvFont? FontFor(string? asset, int? size)
    {
        if (ResolveAsset(asset) is not { } path) return null;

        var key = (path, size ?? DefaultTrueTypeSize);
        if (_fonts.TryGetValue(key, out var cached)) return cached;

        try
        {
            var font = LvFont.FromFile(path, key.Item2);
            _fonts[key] = font;
            return font;
        }
        catch (LvglException ex)
        {
            _diagnostics.Add(ex.Message);
            return null;
        }
    }

    private string? ResolveAsset(string? asset)
    {
        if (string.IsNullOrWhiteSpace(asset)) return null;

        var path = Path.GetFullPath(Path.Combine(AssetDirectory, asset));
        if (File.Exists(path)) return path;

        _diagnostics.Add($"Asset '{asset}' was not found in {AssetDirectory}.");
        return null;
    }

    private static bool TryParseColor(string? text, out LvColor color)
    {
        color = default;
        return !string.IsNullOrWhiteSpace(text) && LvColor.TryParse(text, out color);
    }

    /// <summary>
    /// Frees the shared styles and fonts. Call only after the widgets built from the document have
    /// been deleted - they point at these.
    /// </summary>
    public void Dispose()
    {
        foreach (var style in _styles.Values) style.Dispose();
        foreach (var font in _fonts.Values) font.Dispose();

        _styles.Clear();
        _fonts.Clear();
        _named.Clear();
        _byNode.Clear();
        _keyboards.Clear();
    }
}
