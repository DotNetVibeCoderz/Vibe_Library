# Lvgl.Ui

[API reference](README.md) · generated from the XML documentation; do not edit by hand.

## CSharpUiGenerator

`sealed class` in `LVGL.Net.Ui`

Emits C# that rebuilds a [`UiDocument`](#uidocument) with the LVGL.Net widget API.

The generated type is `partial` and calls a `partial void OnBuilt()` hook at the end
of `Build`. Application code puts event wiring and data binding in the other half of the
class, so regenerating the layout never overwrites hand-written logic.





Shared styles become `LvStyle` properties and TrueType fonts are loaded once and cached.
Both are owned by the generated object; its `Destroy()` deletes the widgets first and
frees the styles and fonts after, the order LVGL requires.





Framework types are written `global::`-qualified, so the output compiles whether or not
the consuming project enables implicit usings.





This produces a file to check in, not a compiler source generator: designer output is edited
through the designer, and having the code on disk keeps the build free of extra tooling.

| Member | Description |
|--------|-------------|
| `CSharpUiGenerator()` |  |
| `string AccessModifier { get; set; }` | Access modifier of the generated class. |
| `string ClassName { get; set; }` | Class name; defaults to the document name. |
| `string Namespace { get; set; }` | Namespace for the generated class. Omitted when null or empty. |
| `string SourcePath { get; set; }` | Source file the code was generated from, mentioned in the header comment. |
| `string Generate(UiDocument document)` | Generates the C# source text. |
| `static string TypeName(UiWidgetType type)` | The LVGL.Net class a widget kind is generated as, e.g. `LvTabPage` for a tab. |

## UiBuilder

`sealed class` in `LVGL.Net.Ui`

Turns a [`UiDocument`](#uidocument) into live LVGL widgets.

Used by the designer to render a true preview, and available at run time for applications that
prefer to ship layouts as data rather than generated code. Both paths apply properties in the
same order, so a preview and a running app look identical.




**Teardown.** The builder owns the shared [`LvStyle`](Lvgl.Styling.md#lvstyle)s and TrueType
[`LvFont`](Lvgl.Drawing.md#lvfont)s it creates, and the widgets point at them. Delete the widgets first
(clear the screen or delete the root), then [`Dispose`](#uibuilder) the builder - the order every
LVGL style follows.

| Member | Description |
|--------|-------------|
| `UiBuilder()` |  |
| `string AssetDirectory { get; set; }` | Folder relative asset paths are resolved against - normally the folder the document was loaded from. Defaults to the current directory. |
| `IReadOnlyList<string> Diagnostics { get; }` | Problems met while building that did not stop it - typically an asset file that is missing. The widget is still created, just without the asset. |
| `IReadOnlyDictionary<string, LvObject> NamedWidgets { get; }` | Widgets that carried a [`Name`](#uinode), keyed by that name. |
| `IReadOnlyDictionary<string, LvStyle> Styles { get; }` | The shared styles built for the document, keyed by style name. |
| `IReadOnlyDictionary<UiNode, LvObject> WidgetsByNode { get; }` | Every widget created by the last [`Build`](#uibuilder), keyed by the node it came from. The designer uses this to map a selection in the tree onto the live widget it drew. |
| `LvObject Build(UiDocument document, LvObject parent = null)` | Creates every widget in `document` under `parent`.<br>`document`: Layout to instantiate.<br>`parent`: Container to build into; `null` uses the active screen.<br>Returns: The container the widgets were added to. |
| `void Dispose()` | Frees the shared styles and fonts. Call only after the widgets built from the document have been deleted - they point at these. |
| `UiBuilder.T Find<T>(string name)` | Looks up a named widget produced by the last [`Build`](#uibuilder). |
| `const int DefaultTrueTypeSize` | Default pixel size of a TrueType font whose node gives no FontSize. |

## UiDocument

`sealed class` in `LVGL.Net.Ui`

A saved screen layout.

This is the single source of truth shared by three consumers: the designer edits it, the
runtime [`UiBuilder`](#uibuilder) turns it into live widgets, and [`CSharpUiGenerator`](#csharpuigenerator)
emits equivalent C#. Keeping one model means a layout previewed in the designer and a layout
built at run time cannot drift apart.





Asset paths ([`Source`](#uinode), [`Font`](#uinode), [`Font`](#uistyle))
are relative to the folder the document is saved in, so a layout and its assets can be moved
or checked in together.

| Member | Description |
|--------|-------------|
| `UiDocument()` |  |
| `string BackgroundColor { get; set; }` | Background colour of the screen, as `#RRGGBB`. |
| `List<UiNode> Children { get; set; }` | Top-level widgets. |
| `int Height { get; set; }` | Target screen height in pixels, used by the designer preview. |
| `string Name { get; set; }` | Screen name; also the generated class name. |
| `List<UiStyle> Styles { get; set; }` | Shared styles that nodes reference by name through [`Styles`](#uinode). |
| `int Version { get; set; }` | File format version, so older documents can be migrated rather than rejected. Version 2 added the 0.2 widgets, shared styles and assets; every version-1 file is a valid version-2 file. |
| `int Width { get; set; }` | Target screen width in pixels, used by the designer preview. |
| `IEnumerable<string> AssetPaths()` | Every asset path referenced by the document, relative to the document folder. |
| `IEnumerable<UiNode> Descendants()` | Depth-first enumeration of every node in the document. |
| `UiNode Find(string name)` | Finds a node by [`Name`](#uinode). |
| `UiStyle FindStyle(string name)` | Finds a shared style by name. |
| `static bool IsValidIdentifier(string name)` | True when `name` can be used as a generated field name. |
| `UiNode ParentOf(UiNode node)` | The parent of `node`, or null when it is top-level or absent. |
| `IReadOnlyList<string> Validate()` | Checks the document for problems that would break code generation or produce a confusing UI: duplicate or invalid names, inverted ranges, malformed colours, misplaced tabs and tiles, dangling style or keyboard references, unusable asset paths.<br>Returns: Human-readable problems; empty when the document is sound. |
| `IReadOnlyList<string> Warnings()` | Advisory problems: things that will build and open but probably do not look the way they were meant to. |
| `const int CurrentVersion` | The format version this build writes. |

## UiJson

`static class` in `LVGL.Net.Ui`

Loads and saves [`UiDocument`](#uidocument) files (`*.lvgl.json`).

Serialisation goes through a source-generated context rather than reflection so the designer and
any AOT-published tool share the same code path and no trimming configuration is required.

| Member | Description |
|--------|-------------|
| `static UiDocument Load(string path)` | Reads a document from disk. |
| `static UiDocument Parse(string json)` | Reads a document from a JSON string. |
| `static void Save(UiDocument document, string path)` | Writes a document to disk. |
| `static string ToJson(UiDocument document)` | Serialises a document to an indented JSON string. |
| `const string FileExtension` | Conventional file extension for saved layouts. |

## UiNode

`sealed class` in `LVGL.Net.Ui`

One widget in a [`UiDocument`](#uidocument).

Properties are nullable to distinguish "leave at the LVGL default" from "set to zero". Only
values the designer actually changed are written to disk and emitted into generated code.

| Member | Description |
|--------|-------------|
| `UiNode()` |  |
| `string AccentColor { get; set; }` | The widget's highlight colour: the indicator of sliders, bars, arcs and spinners, the light of an LED. |
| `bool AcceptsChildren { get; }` | True when this widget kind can contain arbitrary child widgets. |
| `LvAlign? Align { get; set; }` | Alignment inside the parent. `null` means absolute positioning. |
| `string BackgroundColor { get; set; }` |  |
| `string BorderColor { get; set; }` |  |
| `int? BorderWidth { get; set; }` |  |
| `LvChartType? ChartType { get; set; }` | Plot style for charts. |
| `List<UiNode> Children { get; set; }` | Nested widgets. |
| `int? Column { get; set; }` | Grid column of a tile. |
| `int? DecimalPlaces { get; set; }` | Digits a spinbox shows after the decimal point. |
| `int? Digits { get; set; }` | Digits a spinbox shows. |
| `string Font { get; set; }` | TrueType font asset, sized by [`FontSize`](#uinode) (16 when unset). |
| `int? FontSize { get; set; }` |  |
| `int? Height { get; set; }` |  |
| `bool Hidden { get; set; }` | Starts hidden. |
| `int? ImageScale { get; set; }` | Image zoom where 256 is 100 %. |
| `bool IsPageHost { get; }` | True for the structural containers whose children must be tabs or tiles. |
| `LvKeyboardMode? KeyboardMode { get; set; }` | Key layout of a keyboard. |
| `int? MajorTickEvery { get; set; }` | Every n-th tick of a scale is major and labelled. |
| `int? Maximum { get; set; }` |  |
| `int? Minimum { get; set; }` |  |
| `string Name { get; set; }` | Field name in generated code. Optional; unnamed nodes are still created. |
| `List<string> Options { get; set; }` | Entries for drop-downs and rollers; buttons of a list (a line starting with `#` is a section heading); rows of a table (cells separated by `\|`). |
| `int? Padding { get; set; }` |  |
| `int? PointCount { get; set; }` | Samples retained per chart series. |
| `int? Radius { get; set; }` |  |
| `int? Rotation { get; set; }` | Image rotation in tenths of a degree. |
| `int? Row { get; set; }` | Grid row of a tile. |
| `LvScaleMode? ScaleMode { get; set; }` | Shape of a scale. |
| `List<string> SeriesColors { get; set; }` | Series colours (`#RRGGBB`) for charts. |
| `string Source { get; set; }` | Image file asset, relative to the document. |
| `List<string> Styles { get; set; }` | Names of shared [`UiStyle`](#uistyle)s applied before the node's own properties. |
| `string Symbol { get; set; }` | Name of an `LvSymbols` glyph for an image to show instead of a file. |
| `LvDir? TabBarPosition { get; set; }` | Which edge a tab view's tab bar sits on. |
| `string Target { get; set; }` | Name of the TextArea a keyboard types into. |
| `string Text { get; set; }` | Caption for labels, buttons and checkboxes; title of a tab. |
| `string TextColor { get; set; }` |  |
| `int? TickCount { get; set; }` | Total ticks of a scale. |
| `UiWidgetType Type { get; set; }` | Which widget to create. |
| `int? Value { get; set; }` | Initial value for sliders, bars, arcs and spinboxes; brightness of an LED. |
| `int? Width { get; set; }` |  |
| `int X { get; set; }` |  |
| `int Y { get; set; }` |  |
| `IEnumerable<ValueTuple<string, string>> Colors()` | Colour-valued properties, for validation and property-grid binding. |
| `IEnumerable<ValueTuple<bool, string>> ListEntries()` | List entries split into headings and buttons: `(true, "Network")` for a heading. |
| `IReadOnlyList<IReadOnlyList<string>> TableRows()` | Table rows split into cells. |
| `string ToString()` |  |

## UiStyle

`sealed class` in `LVGL.Net.Ui`

A shared style: a set of properties defined once and applied to many widgets.

Built into one `LvStyle` that every referencing widget points at, which is both cheaper
for LVGL and easier to restyle than repeating the properties on each node. Properties set on
a node itself win over its styles, as in LVGL.

| Member | Description |
|--------|-------------|
| `UiStyle()` |  |
| `string BackgroundColor { get; set; }` |  |
| `int? BackgroundOpacity { get; set; }` | Background opacity, 0 to 255. |
| `string BorderColor { get; set; }` |  |
| `int? BorderWidth { get; set; }` |  |
| `string Font { get; set; }` | TrueType font asset, sized by [`FontSize`](#uistyle). |
| `int? FontSize { get; set; }` |  |
| `string Name { get; set; }` | Name nodes refer to; also the generated field name. |
| `int? Padding { get; set; }` |  |
| `int? Radius { get; set; }` |  |
| `string TextColor { get; set; }` |  |
| `IEnumerable<ValueTuple<string, string>> Colors()` | Colour-valued properties, for validation. |
| `string ToString()` |  |

## UiSymbols

`static class` in `LVGL.Net.Ui`

The [`LvSymbols`](Lvgl.md#lvsymbols) glyphs by name, so a layout document can say `"Symbol": "Wifi"`.

Built from [`LvSymbols`](Lvgl.md#lvsymbols)' own properties rather than a second hand-kept list, so a
symbol added there is available to layouts without another edit. Reflecting over a type named
with `typeof` is something the trimmer can see, so this stays AOT-safe.

| Member | Description |
|--------|-------------|
| `IReadOnlyList<string> Names { get; }` | Every symbol name, sorted. |
| `static string Glyph(string name)` | The glyph string for a symbol name. |
| `static bool IsKnown(string name)` | True when `name` is an [`LvSymbols`](Lvgl.md#lvsymbols) property. |

## UiWidgetType

`enum` in `LVGL.Net.Ui`

Widget kinds the designer and the loader understand.

Saved as names, not numbers, so members can be added anywhere without breaking files. New
kinds go at the end anyway, to keep diffs of this list readable.

| Value | Description |
|--------|-------------|
| `Panel = 0` |  |
| `Label = 1` |  |
| `Button = 2` |  |
| `Slider = 3` |  |
| `Bar = 4` |  |
| `Arc = 5` |  |
| `Switch = 6` |  |
| `Checkbox = 7` |  |
| `Dropdown = 8` |  |
| `Roller = 9` |  |
| `TextArea = 10` |  |
| `Chart = 11` |  |
| `Image = 12` |  |
| `TabView = 13` | Tabs; its children must be [`Tab`](#uiwidgettype) nodes. |
| `Tab = 14` | One page of a [`TabView`](#uiwidgettype); [`Text`](#uinode) is the tab title. |
| `List = 15` |  |
| `TileView = 16` | Swipeable pages; its children must be [`Tile`](#uiwidgettype) nodes. |
| `Tile = 17` | One page of a [`TileView`](#uiwidgettype) at [`Column`](#uinode), [`Row`](#uinode). |
| `Keyboard = 18` |  |
| `Spinner = 19` |  |
| `Led = 20` |  |
| `Scale = 21` |  |
| `Table = 22` |  |
| `Spinbox = 23` |  |

