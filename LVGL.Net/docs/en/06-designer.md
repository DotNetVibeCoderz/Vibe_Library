# The designer

A WPF application for laying out LVGL screens visually, with a preview that is LVGL itself rather
than a WPF approximation of it.

```bash
dotnet run --project tools/LVGL.Designer
```

Windows only — that is what WPF means. Nothing else in the repository depends on it, and layouts it
produces run anywhere.

## The window

| Area | Purpose |
|------|---------|
| Toolbox (left) | Double-click a widget type to add it |
| Preview (centre) | Live LVGL rendering; the selected widget is outlined |
| Outline (right, top) | The widget tree; select, delete, duplicate |
| Properties (right, bottom) | Edit the selected widget |
| Toolbar | New, Open, Save, Export C#, and the target screen size |

## Why the preview is trustworthy

The designer hosts a real LVGL instance through `HeadlessBackend` and copies its frames into a
`WriteableBitmap`. Font metrics, corner radii, gradients, the exact way LVGL rounds a layout — all
of it is the same code that will run on the device.

LVGL is single-threaded, so it runs on the WPF dispatcher thread, driven by a `DispatcherTimer` at
about 30 Hz. Property edits are debounced by 150 ms and then rebuild the widget tree from scratch;
at designer scale that is cheaper and more honest than diffing.

If the native library has not been built, the preview area explains what to do. **Editing, saving
and C# export all keep working** — only the rendered preview needs LVGL.

## The file format

Layouts are saved as `*.lvgl.json`: a plain, diff-friendly document.

```json
{
  "Version": 1,
  "Name": "Dashboard",
  "Width": 800,
  "Height": 480,
  "BackgroundColor": "#0F1720",
  "Children": [
    {
      "Type": "Label",
      "Name": "TitleLabel",
      "Text": "Hello",
      "Align": "TopMid",
      "Y": 28,
      "FontSize": 28
    }
  ]
}
```

Enums are written as names, so a future LVGL reordering cannot silently change a saved layout.
Optional properties are omitted entirely when unset — that is what preserves the difference between
"leave at the LVGL default" and "explicitly zero".

## Two ways to use a layout

**Load it at run time**, shipping the layout as data:

```csharp
var document = UiJson.Load("Dashboard.lvgl.json");
var builder = new UiBuilder();
builder.Build(document, app.Screen);

var title = builder.Find<LvLabel>("TitleLabel");
var button = builder.Find<LvButton>("ActionButton");
button!.Clicked += (_, _) => title!.Text = "clicked";
```

**Or export C#** — *Export C#* in the toolbar writes a file like:

```csharp
public partial class Dashboard
{
    public const int DesignWidth = 800;
    public const int DesignHeight = 480;

    public LvObject Root { get; private set; } = null!;
    public LvLabel TitleLabel { get; private set; } = null!;

    public LvObject Build(LvObject? parent = null)
    {
        Root = parent ?? LvScreen.Active();
        Root.SetBackgroundColor(LvColor.FromRgb(0x0F1720u));

        var titleLabel = new LvLabel(Root, "Hello");
        titleLabel.Align(LvAlign.TopMid, 0, 28);
        titleLabel.SetFontSize(28);
        TitleLabel = titleLabel;

        OnBuilt();
        return Root;
    }

    partial void OnBuilt();
}
```

The class is `partial` and calls `OnBuilt()` at the end, so your event wiring lives in the other
half and regenerating the layout never overwrites it:

```csharp
public partial class Dashboard
{
    partial void OnBuilt()
    {
        ActionButton.Clicked += (_, _) => TitleLabel.Text = "clicked";
    }
}
```

Both paths go through the same `UiDocument` model, so they produce identical widget trees. Choose
run-time loading when layouts change without a rebuild; choose generated C# when you want compile-
time names and no JSON parsing on a constrained device.

## Naming rules

A widget's name becomes a C# property, so it must be a valid identifier and unique in the document.
*Export C#* refuses to run on a document that violates this and lists every problem; the same check
is available programmatically:

```csharp
foreach (var problem in document.Validate()) Console.WriteLine(problem);
```

Validation also catches inverted ranges and malformed colours.

## Current limitations

- Selection and editing happen through the outline and property panel; the preview is not yet
  direct-manipulation (no drag to move or resize).
- Nesting is supported for `Panel` containers only.
- Event handlers are written in code, not assigned in the designer — deliberately, so generated
  code never contains application logic.
