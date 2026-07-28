using System.Text.Json.Serialization;

namespace Lvgl.Ui;

/// <summary>Widget kinds the designer and the loader understand.</summary>
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
}

/// <summary>
/// A saved screen layout.
/// </summary>
/// <remarks>
/// This is the single source of truth shared by three consumers: the WPF designer edits it, the
/// runtime <see cref="UiBuilder"/> turns it into live widgets, and <see cref="CSharpUiGenerator"/>
/// emits equivalent C#. Keeping one model means a layout previewed in the designer and a layout
/// built at run time cannot drift apart.
/// </remarks>
public sealed class UiDocument
{
    /// <summary>File format version, so older documents can be migrated rather than rejected.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Screen name; also the generated class name.</summary>
    public string Name { get; set; } = "Screen";

    /// <summary>Target screen width in pixels, used by the designer preview.</summary>
    public int Width { get; set; } = 800;

    /// <summary>Target screen height in pixels, used by the designer preview.</summary>
    public int Height { get; set; } = 480;

    /// <summary>Background colour of the screen, as <c>#RRGGBB</c>.</summary>
    public string? BackgroundColor { get; set; }

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

    /// <summary>
    /// Checks the document for problems that would break code generation or produce a confusing
    /// UI: duplicate or invalid names, inverted ranges, malformed colours.
    /// </summary>
    /// <returns>Human-readable problems; empty when the document is sound.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

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
        }

        if (Width <= 0 || Height <= 0) problems.Add($"The screen size {Width}x{Height} is not usable.");

        return problems;
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

    /// <summary>Caption for labels, buttons and checkboxes.</summary>
    public string? Text { get; set; }

    public string? BackgroundColor { get; set; }

    public string? TextColor { get; set; }

    public string? BorderColor { get; set; }

    public int? BorderWidth { get; set; }

    public int? Radius { get; set; }

    public int? Padding { get; set; }

    public int? FontSize { get; set; }

    /// <summary>Initial value for sliders, bars and arcs.</summary>
    public int? Value { get; set; }

    public int? Minimum { get; set; }

    public int? Maximum { get; set; }

    /// <summary>Starts hidden.</summary>
    public bool Hidden { get; set; }

    /// <summary>Entries for drop-downs and rollers.</summary>
    public List<string> Options { get; set; } = [];

    /// <summary>Series colours (<c>#RRGGBB</c>) for charts.</summary>
    public List<string> SeriesColors { get; set; } = [];

    /// <summary>Plot style for charts.</summary>
    public LvChartType? ChartType { get; set; }

    /// <summary>Samples retained per chart series.</summary>
    public int? PointCount { get; set; }

    /// <summary>Nested widgets.</summary>
    public List<UiNode> Children { get; set; } = [];

    /// <summary>Colour-valued properties, for validation and property-grid binding.</summary>
    public IEnumerable<(string Label, string? Value)> Colors()
    {
        yield return ("background", BackgroundColor);
        yield return ("text", TextColor);
        yield return ("border", BorderColor);

        foreach (var color in SeriesColors) yield return ("series", color);
    }

    /// <summary>True when this widget kind can contain children.</summary>
    [JsonIgnore]
    public bool AcceptsChildren => Type is UiWidgetType.Panel;

    public override string ToString() => string.IsNullOrEmpty(Name) ? Type.ToString() : $"{Type} '{Name}'";
}
