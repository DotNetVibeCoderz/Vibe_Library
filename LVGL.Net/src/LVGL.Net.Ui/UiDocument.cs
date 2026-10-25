using System.Text.Json.Serialization;

namespace Lvgl.Ui;

/// <summary>Widget kinds the designer and the loader understand.</summary>
/// <remarks>
/// Saved as names, not numbers, so members can be added anywhere without breaking files. New
/// kinds go at the end anyway, to keep diffs of this list readable.
/// </remarks>
public enum UiWidgetType
{
    Panel,
    Label,
    Button,
    Slider,
    Bar,
    Arc,
    Switch,
    Checkbox,
    Dropdown,
    Roller,
    TextArea,
    Chart,
    Image,
    /// <summary>Tabs; its children must be <see cref="Tab"/> nodes.</summary>
    TabView,
    /// <summary>One page of a <see cref="TabView"/>; <see cref="UiNode.Text"/> is the tab title.</summary>
    Tab,
    List,
    /// <summary>Swipeable pages; its children must be <see cref="Tile"/> nodes.</summary>
    TileView,
    /// <summary>One page of a <see cref="TileView"/> at <see cref="UiNode.Column"/>, <see cref="UiNode.Row"/>.</summary>
    Tile,
    Keyboard,
    Spinner,
    Led,
    Scale,
    Table,
    Spinbox,
}

/// <summary>
/// A saved screen layout.
/// </summary>
/// <remarks>
/// <para>
/// This is the single source of truth shared by three consumers: the designer edits it, the
/// runtime <see cref="UiBuilder"/> turns it into live widgets, and <see cref="CSharpUiGenerator"/>
/// emits equivalent C#. Keeping one model means a layout previewed in the designer and a layout
/// built at run time cannot drift apart.
/// </para>
/// <para>
/// Asset paths (<see cref="UiNode.Source"/>, <see cref="UiNode.Font"/>, <see cref="UiStyle.Font"/>)
/// are relative to the folder the document is saved in, so a layout and its assets can be moved
/// or checked in together.
/// </para>
/// </remarks>
public sealed class UiDocument
{
    /// <summary>The format version this build writes.</summary>
    public const int CurrentVersion = 2;

    /// <summary>
    /// File format version, so older documents can be migrated rather than rejected. Version 2
    /// added the 0.2 widgets, shared styles and assets; every version-1 file is a valid version-2
    /// file.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>Screen name; also the generated class name.</summary>
    public string Name { get; set; } = "Screen";

    /// <summary>Target screen width in pixels, used by the designer preview.</summary>
    public int Width { get; set; } = 800;

    /// <summary>Target screen height in pixels, used by the designer preview.</summary>
    public int Height { get; set; } = 480;

    /// <summary>Background colour of the screen, as <c>#RRGGBB</c>.</summary>
    public string? BackgroundColor { get; set; }

    /// <summary>Shared styles that nodes reference by name through <see cref="UiNode.Styles"/>.</summary>
    public List<UiStyle> Styles { get; set; } = [];

    /// <summary>Top-level widgets.</summary>
    public List<UiNode> Children { get; set; } = [];

    /// <summary>Depth-first enumeration of every node in the document.</summary>
    public IEnumerable<UiNode> Descendants()
    {
        var stack = new Stack<UiNode>(Children.AsEnumerable().Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
        }
    }

    /// <summary>Finds a node by <see cref="UiNode.Name"/>.</summary>
    public UiNode? Find(string name) =>
        Descendants().FirstOrDefault(node => string.Equals(node.Name, name, StringComparison.Ordinal));

    /// <summary>Finds a shared style by name.</summary>
    public UiStyle? FindStyle(string name) =>
        Styles.FirstOrDefault(style => string.Equals(style.Name, name, StringComparison.Ordinal));

    /// <summary>The parent of <paramref name="node"/>, or null when it is top-level or absent.</summary>
    public UiNode? ParentOf(UiNode node)
    {
        foreach (var candidate in Descendants())
        {
            if (candidate.Children.Contains(node)) return candidate;
        }

        return null;
    }

    /// <summary>Every asset path referenced by the document, relative to the document folder.</summary>
    public IEnumerable<string> AssetPaths()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var style in Styles)
        {
            if (!string.IsNullOrWhiteSpace(style.Font) && seen.Add(style.Font)) yield return style.Font;
        }

        foreach (var node in Descendants())
        {
            if (!string.IsNullOrWhiteSpace(node.Source) && seen.Add(node.Source)) yield return node.Source;
            if (!string.IsNullOrWhiteSpace(node.Font) && seen.Add(node.Font)) yield return node.Font;
        }
    }

    /// <summary>
    /// Checks the document for problems that would break code generation or produce a confusing
    /// UI: duplicate or invalid names, inverted ranges, malformed colours, misplaced tabs and
    /// tiles, dangling style or keyboard references, unusable asset paths.
    /// </summary>
    /// <returns>Human-readable problems; empty when the document is sound.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        ValidateStyles(problems);

        foreach (var node in Descendants())
        {
            if (!string.IsNullOrEmpty(node.Name))
            {
                if (!IsValidIdentifier(node.Name))
                {
                    problems.Add($"'{node.Name}' is not a valid C# identifier, so no field can be generated for it.");
                }
                else if (!seen.Add(node.Name))
                {
                    problems.Add($"The name '{node.Name}' is used more than once.");
                }
            }

            if (node.Minimum is { } min && node.Maximum is { } max && min > max)
            {
                problems.Add($"{Describe(node)} has minimum {min} greater than maximum {max}.");
            }

            foreach (var (label, value) in node.Colors())
            {
                if (value is not null && !Drawing.LvColor.TryParse(value, out _))
                {
                    problems.Add($"{Describe(node)} has an invalid {label} colour '{value}'.");
                }
            }

            foreach (var styleName in node.Styles)
            {
                if (FindStyle(styleName) is null) problems.Add($"{Describe(node)} uses the style '{styleName}', which does not exist.");
            }

            // Tab and tile hosts have children too, but theirs are checked by ValidateStructure.
            if (node.Children.Count > 0 && !node.AcceptsChildren && !node.IsPageHost)
            {
                problems.Add($"{Describe(node)} is a {node.Type}, which cannot contain other widgets.");
            }

            ValidateStructure(node, problems);
            ValidateAsset(node.Source, $"{Describe(node)}'s image", problems);
            ValidateAsset(node.Font, $"{Describe(node)}'s font", problems);
        }

        foreach (var node in Children)
        {
            if (node.Type is UiWidgetType.Tab or UiWidgetType.Tile)
            {
                problems.Add($"{Describe(node)} is a {node.Type}, which must sit inside a {(node.Type == UiWidgetType.Tab ? "TabView" : "TileView")}.");
            }
        }

        if (Width <= 0 || Height <= 0) problems.Add($"The screen size {Width}x{Height} is not usable.");

        return problems;
    }

    private void ValidateStyles(List<string> problems)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var style in Styles)
        {
            if (!IsValidIdentifier(style.Name))
            {
                problems.Add($"The style name '{style.Name}' is not a valid C# identifier.");
            }
            else if (!names.Add(style.Name))
            {
                problems.Add($"The style name '{style.Name}' is used more than once.");
            }

            foreach (var (label, value) in style.Colors())
            {
                if (value is not null && !Drawing.LvColor.TryParse(value, out _))
                {
                    problems.Add($"Style '{style.Name}' has an invalid {label} colour '{value}'.");
                }
            }

            ValidateAsset(style.Font, $"Style '{style.Name}''s font", problems);
        }
    }

    private void ValidateStructure(UiNode node, List<string> problems)
    {
        switch (node.Type)
        {
            case UiWidgetType.TabView:
                foreach (var child in node.Children.Where(c => c.Type != UiWidgetType.Tab))
                {
                    problems.Add($"{Describe(child)} is inside the TabView {Describe(node)} but is not a Tab. Put it inside one of the tabs.");
                }
                break;

            case UiWidgetType.TileView:
                var cells = new HashSet<(int, int)>();
                foreach (var child in node.Children)
                {
                    if (child.Type != UiWidgetType.Tile)
                    {
                        problems.Add($"{Describe(child)} is inside the TileView {Describe(node)} but is not a Tile. Put it inside one of the tiles.");
                        continue;
                    }

                    var cell = (child.Column ?? 0, child.Row ?? 0);
                    if (cell.Item1 is < 0 or > 255 || cell.Item2 is < 0 or > 255)
                    {
                        problems.Add($"{Describe(child)} is at column {cell.Item1}, row {cell.Item2}; both must be 0 to 255.");
                    }
                    else if (!cells.Add(cell))
                    {
                        problems.Add($"Two tiles of {Describe(node)} share column {cell.Item1}, row {cell.Item2}.");
                    }
                }
                break;

            case UiWidgetType.Tab:
            case UiWidgetType.Tile:
                var parent = ParentOf(node);
                var expected = node.Type == UiWidgetType.Tab ? UiWidgetType.TabView : UiWidgetType.TileView;
                if (parent is not null && parent.Type != expected)
                {
                    problems.Add($"{Describe(node)} is a {node.Type}, which must sit directly inside a {expected}.");
                }
                break;

            case UiWidgetType.Keyboard:
                if (!string.IsNullOrEmpty(node.Target) && Find(node.Target) is not { Type: UiWidgetType.TextArea })
                {
                    problems.Add($"{Describe(node)} types into '{node.Target}', which is not a TextArea in this layout.");
                }
                break;

            case UiWidgetType.Spinbox:
                if (node.Digits is { } digits && digits is < 1 or > 10)
                {
                    problems.Add($"{Describe(node)} has {digits} digits; a spinbox shows 1 to 10.");
                }

                if (node.DecimalPlaces is { } places && (places < 0 || places >= (node.Digits ?? 5)))
                {
                    problems.Add($"{Describe(node)} has {places} decimal places, which must be fewer than its {node.Digits ?? 5} digits.");
                }
                break;

            case UiWidgetType.Image:
                if (!string.IsNullOrEmpty(node.Symbol) && !UiSymbols.IsKnown(node.Symbol))
                {
                    problems.Add($"{Describe(node)} uses the symbol '{node.Symbol}', which is not one of LvSymbols.");
                }
                break;
        }
    }

    private static void ValidateAsset(string? path, string what, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        if (Path.IsPathRooted(path) || path.Contains(':', StringComparison.Ordinal))
        {
            problems.Add($"{what} '{path}' is an absolute path. Assets are stored relative to the layout file so the two can move together - import it into the layout's folder instead.");
        }
        else if (path.Replace('\\', '/').Split('/').Any(part => part == ".."))
        {
            problems.Add($"{what} '{path}' points outside the layout's folder.");
        }
    }

    /// <summary>
    /// Advisory problems: things that will build and open but probably do not look the way they
    /// were meant to.
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="Validate"/> because these are judgement calls, not errors -
    /// a large alignment offset is legal, just usually a mistake. They are reported rather than
    /// enforced so an unusual-but-deliberate layout is still allowed.
    /// </remarks>
    public IReadOnlyList<string> Warnings()
    {
        var warnings = new List<string>();

        foreach (var node in Descendants())
        {
            // The mistake this exists for: setting Align *and* absolute-looking X/Y. When a
            // widget is aligned, X and Y are offsets from the anchor - so Align=TopMid with
            // X=240 on a 480-wide screen puts it 240px right of centre, off the screen. It is a
            // natural assumption to make and produces a layout that validates and renders wrong.
            if (node.Align is { } align && align != LvAlign.Default)
            {
                if (Math.Abs(node.X) >= Width / 2 || Math.Abs(node.Y) >= Height / 2)
                {
                    warnings.Add(
                        $"{Describe(node)} sets Align={align} together with X={node.X}, Y={node.Y}. " +
                        "When a widget is aligned those are offsets from the anchor, not absolute " +
                        "coordinates - this offset is large enough to push it off screen. Either " +
                        "drop the Align and keep the coordinates, or keep the Align and use a small offset.");
                }
            }

            if (node.Width is { } width && width > Width)
            {
                warnings.Add($"{Describe(node)} is {width} wide on a {Width} wide screen.");
            }

            if (node.Height is { } height && height > Height)
            {
                warnings.Add($"{Describe(node)} is {height} tall on a {Height} tall screen.");
            }

            // Unaligned widgets are positioned from the top left, so a position past the screen
            // edge simply puts them out of view.
            if (node.Align is null or LvAlign.Default && (node.X >= Width || node.Y >= Height))
            {
                warnings.Add($"{Describe(node)} is positioned at ({node.X},{node.Y}), outside the {Width}x{Height} screen.");
            }

            if (node.Type == UiWidgetType.Keyboard && string.IsNullOrEmpty(node.Target))
            {
                warnings.Add($"{Describe(node)} is not connected to a TextArea, so its keys type nowhere until code sets one.");
            }

            if (node.Type == UiWidgetType.Image && string.IsNullOrEmpty(node.Source) && string.IsNullOrEmpty(node.Symbol))
            {
                warnings.Add($"{Describe(node)} has neither a Source nor a Symbol, so it shows nothing.");
            }
        }

        return warnings;
    }

    private static string Describe(UiNode node) =>
        string.IsNullOrEmpty(node.Name) ? $"An unnamed {node.Type}" : $"'{node.Name}'";

    /// <summary>True when <paramref name="name"/> can be used as a generated field name.</summary>
    public static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!char.IsLetter(name[0]) && name[0] != '_') return false;

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }

        return true;
    }
}

/// <summary>
/// A shared style: a set of properties defined once and applied to many widgets.
/// </summary>
/// <remarks>
/// Built into one <c>LvStyle</c> that every referencing widget points at, which is both cheaper
/// for LVGL and easier to restyle than repeating the properties on each node. Properties set on
/// a node itself win over its styles, as in LVGL.
/// </remarks>
public sealed class UiStyle
{
    /// <summary>Name nodes refer to; also the generated field name.</summary>
    public string Name { get; set; } = "Style";

    public string? BackgroundColor { get; set; }

    /// <summary>Background opacity, 0 to 255.</summary>
    public int? BackgroundOpacity { get; set; }

    public string? TextColor { get; set; }

    public string? BorderColor { get; set; }

    public int? BorderWidth { get; set; }

    public int? Radius { get; set; }

    public int? Padding { get; set; }

    public int? FontSize { get; set; }

    /// <summary>TrueType font asset, sized by <see cref="FontSize"/>.</summary>
    public string? Font { get; set; }

    /// <summary>Colour-valued properties, for validation.</summary>
    public IEnumerable<(string Label, string? Value)> Colors()
    {
        yield return ("background", BackgroundColor);
        yield return ("text", TextColor);
        yield return ("border", BorderColor);
    }

    public override string ToString() => Name;
}

/// <summary>One widget in a <see cref="UiDocument"/>.</summary>
/// <remarks>
/// Properties are nullable to distinguish "leave at the LVGL default" from "set to zero". Only
/// values the designer actually changed are written to disk and emitted into generated code.
/// </remarks>
public sealed class UiNode
{
    /// <summary>Which widget to create.</summary>
    public UiWidgetType Type { get; set; } = UiWidgetType.Panel;

    /// <summary>Field name in generated code. Optional; unnamed nodes are still created.</summary>
    public string? Name { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    /// <summary>Alignment inside the parent. <see langword="null"/> means absolute positioning.</summary>
    public LvAlign? Align { get; set; }

    /// <summary>Caption for labels, buttons and checkboxes; title of a tab.</summary>
    public string? Text { get; set; }

    public string? BackgroundColor { get; set; }

    public string? TextColor { get; set; }

    public string? BorderColor { get; set; }

    /// <summary>
    /// The widget's highlight colour: the indicator of sliders, bars, arcs and spinners, the light
    /// of an LED.
    /// </summary>
    public string? AccentColor { get; set; }

    public int? BorderWidth { get; set; }

    public int? Radius { get; set; }

    public int? Padding { get; set; }

    public int? FontSize { get; set; }

    /// <summary>TrueType font asset, sized by <see cref="FontSize"/> (16 when unset).</summary>
    public string? Font { get; set; }

    /// <summary>Names of shared <see cref="UiStyle"/>s applied before the node's own properties.</summary>
    public List<string> Styles { get; set; } = [];

    /// <summary>Initial value for sliders, bars, arcs and spinboxes; brightness of an LED.</summary>
    public int? Value { get; set; }

    public int? Minimum { get; set; }

    public int? Maximum { get; set; }

    /// <summary>Starts hidden.</summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Entries for drop-downs and rollers; buttons of a list (a line starting with <c>#</c> is a
    /// section heading); rows of a table (cells separated by <c>|</c>).
    /// </summary>
    public List<string> Options { get; set; } = [];

    /// <summary>Series colours (<c>#RRGGBB</c>) for charts.</summary>
    public List<string> SeriesColors { get; set; } = [];

    /// <summary>Plot style for charts.</summary>
    public LvChartType? ChartType { get; set; }

    /// <summary>Samples retained per chart series.</summary>
    public int? PointCount { get; set; }

    /// <summary>Image file asset, relative to the document.</summary>
    public string? Source { get; set; }

    /// <summary>Name of an <c>LvSymbols</c> glyph for an image to show instead of a file.</summary>
    public string? Symbol { get; set; }

    /// <summary>Image rotation in tenths of a degree.</summary>
    public int? Rotation { get; set; }

    /// <summary>Image zoom where 256 is 100 %.</summary>
    public int? ImageScale { get; set; }

    /// <summary>Which edge a tab view's tab bar sits on.</summary>
    public LvDir? TabBarPosition { get; set; }

    /// <summary>Grid column of a tile.</summary>
    public int? Column { get; set; }

    /// <summary>Grid row of a tile.</summary>
    public int? Row { get; set; }

    /// <summary>Name of the TextArea a keyboard types into.</summary>
    public string? Target { get; set; }

    /// <summary>Key layout of a keyboard.</summary>
    public LvKeyboardMode? KeyboardMode { get; set; }

    /// <summary>Shape of a scale.</summary>
    public LvScaleMode? ScaleMode { get; set; }

    /// <summary>Total ticks of a scale.</summary>
    public int? TickCount { get; set; }

    /// <summary>Every n-th tick of a scale is major and labelled.</summary>
    public int? MajorTickEvery { get; set; }

    /// <summary>Digits a spinbox shows.</summary>
    public int? Digits { get; set; }

    /// <summary>Digits a spinbox shows after the decimal point.</summary>
    public int? DecimalPlaces { get; set; }

    /// <summary>Nested widgets.</summary>
    public List<UiNode> Children { get; set; } = [];

    /// <summary>Colour-valued properties, for validation and property-grid binding.</summary>
    public IEnumerable<(string Label, string? Value)> Colors()
    {
        yield return ("background", BackgroundColor);
        yield return ("text", TextColor);
        yield return ("border", BorderColor);
        yield return ("accent", AccentColor);

        foreach (var color in SeriesColors) yield return ("series", color);
    }

    /// <summary>True when this widget kind can contain arbitrary child widgets.</summary>
    [JsonIgnore]
    public bool AcceptsChildren => Type is UiWidgetType.Panel or UiWidgetType.Tab or UiWidgetType.Tile;

    /// <summary>True for the structural containers whose children must be tabs or tiles.</summary>
    [JsonIgnore]
    public bool IsPageHost => Type is UiWidgetType.TabView or UiWidgetType.TileView;

    /// <summary>
    /// List entries split into headings and buttons: <c>(true, "Network")</c> for a heading.
    /// </summary>
    public IEnumerable<(bool IsHeading, string Text)> ListEntries()
    {
        foreach (var option in Options)
        {
            yield return option.StartsWith('#') ? (true, option.TrimStart('#').Trim()) : (false, option);
        }
    }

    /// <summary>Table rows split into cells.</summary>
    public IReadOnlyList<IReadOnlyList<string>> TableRows() =>
        Options.Select(row => (IReadOnlyList<string>)row.Split('|').Select(cell => cell.Trim()).ToArray()).ToArray();

    public override string ToString() => string.IsNullOrEmpty(Name) ? Type.ToString() : $"{Type} '{Name}'";
}
