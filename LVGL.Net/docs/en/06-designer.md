# The designer

A visual editor for LVGL screens whose preview is LVGL itself, not an approximation of it. Built
with Avalonia, so it runs on Windows, macOS and Linux, a Raspberry Pi desktop included.

```bash
dotnet run --project tools/LVGL.Designer
```

![The designer, editing the smart-home showcase layout](../images/screenshots/designer-design.png)

## The window

| Area | Purpose |
|------|---------|
| Parts (left, top) | Click a widget to add it, into the selected container when it can hold one |
| Outline (left, bottom) | The widget tree; drag rows to re-parent or re-order |
| Board (centre) | The live LVGL preview; select, drag to move, pull the copper pads to resize |
| Inspector (right) | The selected widget's properties, or the screen's; a second page for shared styles |
| Command bar | New, Open, Save, Export C#, Undo/Redo, Design/Code, target display, the assistant |
| Status bar | What just happened, things to check, LVGL status, grid snap, zoom |

The look is taken from the board a layout ends up on: blue solder mask, silkscreen labels, copper
for whatever you are acting on. Every widget has a **reference designator** like a component on a
PCB - `BTN2` is the second button, `LBL1` the first label - shown in the parts list, the outline,
and on the board's selection tag, so the kind of each widget is readable at a glance.

## Why the preview is trustworthy

The designer hosts a real LVGL instance through `HeadlessBackend` and copies its frames into a
bitmap. Font metrics, corner radii, gradients, the exact way LVGL rounds a layout - all of it is
the same code that will run on the device. The selection is drawn as an outline and pads only, with
no tint over the widget, so the colours you see are the colours LVGL draws.

LVGL is single-threaded, so it runs on the Avalonia UI thread, driven by a timer at about 30 Hz.
Every edit rebuilds the widget tree from scratch within a frame or two; at designer scale that is
cheaper and more honest than diffing.

If the native library has not been built, the board explains what to do. **Editing, saving and C#
export all keep working** - only the rendered preview needs LVGL.

## Working on the board

| Do this | To |
|---|---|
| Click a widget | Select it (clicking inside the current selection keeps it, so a container can be dragged by its body) |
| Drag | Move it; the left/top edge snaps to the 8 px grid (the vias on the board) |
| Drag a copper pad | Resize from that edge or corner; the opposite edge stays put |
| Hold **Alt** while dragging | Invert grid snapping for that drag (or toggle **SNAP 8** in the status bar) |
| Arrow keys / **Shift**+arrows | Nudge by 1 / 10 px |
| **Del**, **Ctrl+D**, **Esc** | Delete, duplicate, clear the selection |
| **Ctrl+wheel**, wheel, **Shift+wheel**, middle-drag | Zoom about the pointer, scroll, scroll sideways, pan |
| **Ctrl+0** or the zoom readout | Fit the board to the view |

A whole drag is **one undo step**. Resizing an aligned widget keeps working the way your hand
expects: when you pull the right edge of a centred button, LVGL would re-centre it and move the
left edge too, so the designer writes back the offset that holds the left edge still.

## Containers, tabs and tiles

Panels, tab pages and tiles hold other widgets. A new widget goes into the selected container, or
the nearest container above the selection. A **TabView** must contain only **Tab** nodes and a
**TileView** only **Tile** nodes - the structure LVGL itself has - so adding a tab view also adds
its first tab, and **Tab**/**Tile** in the parts list need a tab or tile view selected.

Drag a row in the outline onto the **middle** of another row to move it inside, or onto its
**top** or **bottom** edge to place it before or after. Moves that would break the structure (a
panel into itself, a label directly into a tab view, a tab out of its tab view) are refused.

## Shared styles

The inspector's **Styles** page holds styles defined once and used by many widgets - a card
background, a caption colour. Tick a style under **Shared styles** on any widget to apply it. A
widget's own properties win over its styles, as in LVGL. Renaming a style updates every widget that
uses it; deleting it detaches them.

![Shared styles in the inspector](../images/screenshots/designer-styles.png)

Styles become `LvStyle` properties in generated code, and `UiBuilder` creates one `LvStyle` per
document style, so LVGL stores the properties once however many widgets use them.

## Images and fonts

An **Image** shows a PNG or BMP file, or one of the built-in `LvSymbols` glyphs. Any widget can
use a TrueType **Font** instead of the built-in Montserrat sizes. **Import...** copies the file into
an `assets` folder next to the layout and stores the relative path, so a layout and its assets move
and get checked in together - which is why importing asks you to save the layout first. Absolute
paths and paths that leave the layout's folder are reported as problems.

## Screen sizes

The display picker in the command bar has the panels LVGL.Net is usually deployed on - the Pi Touch
Display (800 × 480), Touch Display 2 in either orientation, 3.5" and 2.8" SPI panels, 7" and 10" HDMI
panels, a round 480 × 480 panel - or type any size into the two boxes beside it.

## The file format

Layouts are saved as `*.lvgl.json`: a plain, diff-friendly document holding only what was set.

```json
{
  "Version": 2,
  "Name": "Dashboard",
  "Width": 800,
  "Height": 480,
  "BackgroundColor": "#0F1720",
  "Styles": [
    { "Name": "Card", "BackgroundColor": "#1A2430", "Radius": 12, "BorderWidth": 0 }
  ],
  "Children": [
    { "Type": "Label", "Name": "TitleLabel", "Text": "Hello", "Align": "TopMid", "Y": 28, "FontSize": 28 },
    { "Type": "Panel", "Name": "StatusCard", "X": 20, "Y": 80, "Width": 360, "Height": 120, "Styles": [ "Card" ] }
  ]
}
```

Enums are written as names, so LVGL reordering an enum cannot silently change a saved layout.
Unset optional properties, empty lists and `false` flags are omitted entirely - that preserves the
difference between "leave at the LVGL default" and "explicitly zero", and keeps diffs to real
changes. Version-1 files open unchanged and are saved as version 2; a file from a newer designer is
refused rather than half read.

`samples/layouts/smart-home.lvgl.json` is a complete example to open.

## Two ways to use a layout

**Load it at run time**, shipping the layout as data:

```csharp
var document = UiJson.Load("Dashboard.lvgl.json");
using var builder = new UiBuilder { AssetDirectory = AppContext.BaseDirectory };
builder.Build(document, app.Screen);

var title = builder.Find<LvLabel>("TitleLabel");
// ...
// Teardown: delete the widgets first, then dispose the builder (it owns the styles and fonts).
```

**Or export C#** - *Export C#* writes the class and copies the layout's assets next to it:

```csharp
public partial class Dashboard
{
    public const int DesignWidth = 800;
    public const int DesignHeight = 480;

    public LvObject Root { get; private set; } = null!;
    public LvStyle Card { get; private set; } = null!;
    public LvLabel TitleLabel { get; private set; } = null!;

    public LvObject Build(LvObject? parent = null) { /* ... */ OnBuilt(); return Root; }

    /// Deletes the widgets this layout built, then frees its styles and fonts.
    public void Destroy() { /* ... */ }

    partial void OnBuilt();
}
```

The class is `partial` and calls `OnBuilt()` at the end, so your event wiring lives in the other
half and regenerating the layout never overwrites it. The **Event handlers** view in Code mode
writes that other half for you, with a stub per interactive widget.

Both paths go through the same `UiDocument` model and produce identical widget trees. The tests
compile the generator's output - for every widget kind, and together with the handler half - so a
generated file that does not build is a failing test, not a surprise in your project.

## Naming and validation

A widget's name becomes a C# property, so it must be a valid identifier and unique in the document.
*Export C#* refuses a document with problems and lists them all; the status bar shows how many
things there are to check, and clicking it lists them. The same checks are available in code:

```csharp
foreach (var problem in document.Validate()) Console.WriteLine(problem);   // errors
foreach (var warning in document.Warnings()) Console.WriteLine(warning);   // judgement calls
```

Validation covers names, ranges, colours, the tab and tile structure, keyboards whose target is not
a text area, unknown styles and symbols, spinbox digit formats, and asset paths.

## Current limitations

- Event handlers are written in code, not assigned in the designer - deliberately, so generated
  code never contains application logic.
- Message boxes are runtime UI and have no place in a layout document; create them from code with
  `LvMessageBox.Show`.
- Animations are code, not layout: see [Images, fonts and animation](11-images-fonts-animation.md).
