using Lvgl.Ui;

namespace Lvgl.Designer.Core;

/// <summary>
/// Reference designators for widgets, in the manner of a circuit board's silkscreen: <c>BTN2</c>
/// is the second button, <c>LBL1</c> the first label.
/// </summary>
/// <remarks>
/// A designator says what kind of widget something is at a glance, in a fixed-width tag that
/// lines up in the outline and fits on the canvas next to a small widget. It is derived, never
/// stored: numbering follows document order, so it reads like a board's own numbering.
/// </remarks>
public static class Designators
{
    /// <summary>The prefix for a widget kind.</summary>
    public static string Prefix(UiWidgetType type) => type switch
    {
        UiWidgetType.Panel => "PNL",
        UiWidgetType.Label => "LBL",
        UiWidgetType.Button => "BTN",
        UiWidgetType.Slider => "SLD",
        UiWidgetType.Bar => "BAR",
        UiWidgetType.Arc => "ARC",
        UiWidgetType.Switch => "SW",
        UiWidgetType.Checkbox => "CHK",
        UiWidgetType.Dropdown => "DD",
        UiWidgetType.Roller => "ROL",
        UiWidgetType.TextArea => "TXT",
        UiWidgetType.Chart => "CHT",
        UiWidgetType.Image => "IMG",
        UiWidgetType.TabView => "TBV",
        UiWidgetType.Tab => "TAB",
        UiWidgetType.List => "LST",
        UiWidgetType.TileView => "TLV",
        UiWidgetType.Tile => "TIL",
        UiWidgetType.Keyboard => "KBD",
        UiWidgetType.Spinner => "SPN",
        UiWidgetType.Led => "LED",
        UiWidgetType.Scale => "SCL",
        UiWidgetType.Table => "TBL",
        UiWidgetType.Spinbox => "SBX",
        _ => "OBJ",
    };

    /// <summary>The designator of <paramref name="node"/> within <paramref name="document"/>.</summary>
    public static string For(UiDocument document, UiNode node)
    {
        var index = 0;
        foreach (var candidate in document.Descendants())
        {
            if (candidate.Type != node.Type) continue;
            index++;
            if (ReferenceEquals(candidate, node)) return Prefix(node.Type) + index;
        }

        return Prefix(node.Type);
    }

    /// <summary>Designators for every node at once - one pass instead of one per node.</summary>
    public static Dictionary<UiNode, string> All(UiDocument document)
    {
        var counters = new Dictionary<UiWidgetType, int>();
        var result = new Dictionary<UiNode, string>(ReferenceEqualityComparer.Instance);

        foreach (var node in document.Descendants())
        {
            counters[node.Type] = counters.TryGetValue(node.Type, out var count) ? count + 1 : 1;
            result[node] = Prefix(node.Type) + counters[node.Type];
        }

        return result;
    }

    /// <summary>A one-line description for the toolbox.</summary>
    public static string Describe(UiWidgetType type) => type switch
    {
        UiWidgetType.Panel => "Container for other widgets",
        UiWidgetType.Label => "Text",
        UiWidgetType.Button => "Push button with a caption",
        UiWidgetType.Slider => "Draggable value",
        UiWidgetType.Bar => "Progress or level",
        UiWidgetType.Arc => "Round dial or gauge",
        UiWidgetType.Switch => "On / off",
        UiWidgetType.Checkbox => "Tick box with a caption",
        UiWidgetType.Dropdown => "Pick one from a list",
        UiWidgetType.Roller => "Touch-friendly picker",
        UiWidgetType.TextArea => "Editable text field",
        UiWidgetType.Chart => "Live line, bar or scatter chart",
        UiWidgetType.Image => "PNG, BMP or a symbol",
        UiWidgetType.TabView => "Pages behind tabs",
        UiWidgetType.Tab => "One page of a tab view",
        UiWidgetType.List => "Scrolling menu with headings",
        UiWidgetType.TileView => "Swipeable full-screen pages",
        UiWidgetType.Tile => "One page of a tile view",
        UiWidgetType.Keyboard => "On-screen keyboard for a field",
        UiWidgetType.Spinner => "Busy indicator",
        UiWidgetType.Led => "Status light",
        UiWidgetType.Scale => "Ticks for a gauge or ruler",
        UiWidgetType.Table => "Grid of text",
        UiWidgetType.Spinbox => "Number stepped digit by digit",
        _ => string.Empty,
    };
}
