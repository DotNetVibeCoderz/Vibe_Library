# Widgets and styling

## Lifetime

LVGL owns the object tree. `LvObject` therefore does **not** implement `IDisposable` — disposing a
child whose parent already freed it would be a double free.

```csharp
var panel = new LvPanel(app.Screen, 300, 200);
var label = new LvLabel(panel, "inside the panel");

panel.Delete();      // deletes the panel and the label with it
panel.Clear();       // deletes the children, keeps the panel
```

After deletion `IsAlive` turns false and further property access throws `ObjectDisposedException`,
so a stale reference fails loudly instead of writing to freed memory.

`LvStyle` is the exception: it *is* disposable, because LVGL stores a bare pointer to it. Dispose a
style only after every widget using it has gone. The same rule applies to the other things widgets
point at - a TrueType `LvFont` and a pixel `LvImageSource` - and to a `UiBuilder`, which owns the
styles and fonts of a layout it built.

## The widget set

| Class | LVGL widget | Notes |
|-------|-------------|-------|
| `LvPanel` | `lv_obj` | Plain container; the layout building block |
| `LvLabel` | `lv_label` | `Text`, `LongMode`, `Recolor` for in-line `#RRGGBB text#` colours |
| `LvButton` | `lv_button` | `Text` maintains a centred child label; `IsToggle` |
| `LvSlider` | `lv_slider` | `Value`, `SetRange`, `SetValue(v, animate)` |
| `LvBar` | `lv_bar` | Read-only progress |
| `LvArc` | `lv_arc` | Dial or rotary input; `IsReadOnly`, `Mode` (`Symmetrical` for centre-zero gauges) |
| `LvSwitch` | `lv_switch` | `IsOn` |
| `LvCheckbox` | `lv_checkbox` | `Text`, `IsChecked` |
| `LvDropdown` | `lv_dropdown` | `Options`, `SelectedIndex`, `SelectedOption` |
| `LvRoller` | `lv_roller` | Touch-friendly picker |
| `LvTextArea` | `lv_textarea` | `Text`, `PlaceholderText`, `IsSingleLine` |
| `LvChart` | `lv_chart` | `AddSeries`, `PointCount`, `UpdateMode` |
| `LvImage` | `lv_image` | PNG/BMP file, run-time pixels, or a symbol; `Rotation`, `Scale`, `InnerAlign` |
| `LvTabView` / `LvTabPage` | `lv_tabview` | `AddTab` returns the page; `ActiveTab`, `TabBarPosition` |
| `LvList` / `LvListButton` | `lv_list` | `AddText` for headings, `AddButton(text, symbol)` |
| `LvTileView` / `LvTile` | `lv_tileview` | `AddTile(column, row, allowedDirections)`, `ShowTile` |
| `LvMessageBox` | `lv_msgbox` | `Show(title, text, buttons...)` for a modal dialog; `Close` |
| `LvKeyboard` | `lv_keyboard` | On-screen keys for an `LvTextArea`; `Mode`, `ShowPopovers` |
| `LvSpinner` | `lv_spinner` | Busy indicator; `SetAnimation(duration, sweep)` |
| `LvLed` | `lv_led` | `Color`, `Brightness`, `On`/`Off`/`Toggle` |
| `LvScale` | `lv_scale` | Ticks and labels for gauges and rulers; `Mode`, `SetRange`, tick counts |
| `LvTable` | `lv_table` | `this[row, column]`, `SetRows`, `SetColumnWidth` |
| `LvSpinbox` | `lv_spinbox` | Integer value shown with a decimal point; `Increment`/`Decrement` |
| `LvCanvas` | `lv_canvas` | A pixel buffer drawn from managed code; see [Canvas](#canvas) |
| `LvScreen` | screen object | `Create()`, `Load()` for multi-page apps |

A few of the newer ones in use:

```csharp
var tabs = new LvTabView(app.Screen);
var status = tabs.AddTab("Status");
new LvLabel(status, "on the first page");

var list = new LvList(tabs.AddTab("Settings"));
list.AddText("Connectivity");
list.AddButton("Wi-Fi", LvSymbols.Wifi).Clicked += (_, _) => OpenWifi();

// A gauge: a round scale for the ticks, an arc on top for the value.
var scale = new LvScale(status) { Mode = LvScaleMode.RoundInner, TotalTickCount = 21, MajorTickEvery = 5, AngleRange = 270, Rotation = 135, ShowLabels = true };
scale.SetRange(0, 100);

var field = new LvTextArea(app.Screen) { IsSingleLine = true };
var keyboard = new LvKeyboard(app.Screen) { TextArea = field };

var box = LvMessageBox.Show("Delete profile", "This cannot be undone.", "Cancel", "Delete");
box.Buttons[0].Clicked += (_, _) => box.Close();
```

## Geometry and layout

```csharp
widget.SetPosition(20, 40);
widget.SetSize(200, 48);
widget.Align(LvAlign.Center);
widget.Align(LvAlign.TopRight, -12, 12);      // with an offset
widget.AlignTo(other, LvAlign.OutBottomMid, 0, 8);
widget.Center();
```

Alignment and absolute position are alternatives — LVGL treats the align offset *as* the position,
so setting both makes the explicit coordinates meaningless.

`X` and `Y` are relative to the parent's content area. `GetBounds()` returns where the widget really
is on the display - after every ancestor's padding and scrolling - which is what hit-testing and
overlays need. `ZIndex` reads and changes the drawing order among siblings.

### Special coordinates

LVGL encodes percentages and content-sizing in the high bits of an ordinary `int`:

```csharp
widget.Width = LvCoord.Percent(50);
widget.SetSize(LvCoord.SizeContent, LvCoord.SizeContent);   // shrink to fit
```

**Never do arithmetic on these values.** `LvCoord.Percent(100) - 190` does not mean "100% minus
190 pixels" — it corrupts the encoding and yields a meaningless pixel count. Compute in pixels
instead:

```csharp
panel.SetSize(app.Display.Width - SidebarWidth, app.Display.Height);
```

### Flex

```csharp
container.SetFlexFlow(LvFlexFlow.RowWrap);
container.SetFlexAlign(LvFlexAlign.SpaceBetween, LvFlexAlign.Center, LvFlexAlign.Start);
container.SetGap(rowGap: 12, columnGap: 12);
```

## Styling

Two levels. Local properties, set per widget:

```csharp
button.SetBackgroundColor(LvColor.FromRgb(0x38BDF8u));
button.SetRadius(10);
button.SetPadding(12);
button.SetFontSize(20);
button.SetBorderWidth(0);
```

And shared styles, when the same look repeats:

```csharp
_cardStyle = new LvStyle()
    .BackgroundColor(surface)
    .Radius(14)
    .Padding(16)
    .BorderWidth(0);

card1.AddStyle(_cardStyle);
card2.AddStyle(_cardStyle);
```

LVGL stores one property table and the widgets reference it, rather than each carrying a copy.

### Parts and states

Every style setter takes an optional part and state, which together form LVGL's style selector:

```csharp
slider.SetBackgroundColor(accent, LvPart.Indicator);              // the filled portion
slider.SetBackgroundColor(accent, LvPart.Knob);                   // the handle
button.SetBackgroundColor(accent.Darken(0.35f), LvPart.Main, LvState.Pressed);
toggle.SetBackgroundColor(green, LvPart.Indicator, LvState.Checked);
```

`LvPart` values: `Main`, `Indicator`, `Knob`, `Items`, `Selected`, `Cursor`, `Scrollbar`.
`LvState` values include `Pressed`, `Checked`, `Focused`, `Disabled`, `Hovered`, `Alt`, and
`User1`-`User4` for your own states.

Some parts have a size of their own - a chart's point markers, a slider's knob. `SetPartSize` sets
it; zero hides chart markers:

```csharp
chart.SetPartSize(0, 0, LvPart.Indicator);   // a plain line, no dot on every sample
```

### Colours

`LvColor` is a 24-bit RGB value with a few conveniences:

```csharp
LvColor.FromRgb(0x38BDF8u)
LvColor.Parse("#38BDF8")
LvColor.TryParse(userInput, out var color)      // false instead of throwing
accent.Darken(0.3f)
accent.Lighten(0.2f)
background.ContrastingText()                    // black or white, whichever reads better
```

`SetFontSize` picks one of the built-in Montserrat sizes: 12, 14, 16, 20, 24, 28, 36. A size that
was not compiled into the native library falls back to the default rather than failing. For any
other size or script, load a TrueType font - see
[Images, fonts and animation](11-images-fonts-animation.md).

## Events

```csharp
button.Clicked      += (sender, e) => { };
slider.ValueChanged += (sender, e) => { };
widget.Deleted      += (sender, e) => { };

widget.AddHandler(LvEventCode.LongPressed, OnLongPress);
widget.RemoveHandler(LvEventCode.LongPressed, OnLongPress);
```

`LvEventCode` values are LVGL.Net's own stable identifiers; the native shim translates them to the
loaded build's `lv_event_code_t`, whose ordinals changed during the 9.x series.

An exception thrown from a handler cannot be allowed to unwind into LVGL's C stack, so it is caught
at the boundary and reported through `LvglRuntime.CallbackFailed` - subscribe to log it. Handle
expected failures inside the handler.

Some events carry more than their code. Read these inside the handler - they come from LVGL's
current input state:

```csharp
// Keys reach the focused widget; put it in the keyboard group first.
app.FocusGroup!.Add(board);
LvGroup.Focus(board);
board.AddHandler(LvEventCode.Key, (_, e) =>
{
    if (e.Key == LvKey.Left) MoveLeft();         // LvKey.*, or a Unicode code point
});

// A swipe goes to the nearest widget that does not pass gestures up - which every widget does
// by default, so turn that off on the swipe target.
board.SetFlag(LvObjFlag.GestureBubble, false);
board.AddHandler(LvEventCode.Gesture, (_, e) => Slide(e.GestureDirection));   // LvDir.Left, ...

// Where a tap landed, in screen coordinates.
canvas.Clicked += (_, e) =>
{
    if (e.TryGetPointer(out var x, out var y)) ZoomAt(x - canvas.GetBounds().X1, y - canvas.GetBounds().Y1);
};
```

## Charts

Built for streaming data:

```csharp
var chart = new LvChart(parent);
chart.SetSize(600, 240);
chart.Type = LvChartType.Line;
chart.PointCount = 120;
chart.UpdateMode = LvChartUpdateMode.Shift;      // oldest sample drops off the left
chart.SetRange(LvChartAxis.PrimaryY, 0, 100);

var cpu = chart.AddSeries(LvColor.Blue);
cpu.Fill(LvChartSeries.NoValue);                 // empty: the trace starts with the first sample

// later, once per sample
cpu.AddPoint(value);
```

LVGL keeps the ring buffer internally, so a sample costs one native call and allocates nothing.

Since LVGL 9.5 the grid's division lines are drawn with the **main** part's line style, so
`chart.SetLineColor(color)` colours them (in 9.2 it was `LvPart.Items`). LVGL also draws a marker
on every point by default; `SetPartSize(0, 0, LvPart.Indicator)` turns them off.

## Canvas

`LvCanvas` is a widget showing a pixel buffer that managed code draws into: plots, heat maps,
fractals, game boards, waveforms - anything cheaper as pixels than as widgets.

```csharp
var plot = new LvCanvas(parent, 400, 240);       // ARGB8888, cleared to transparent
plot.Fill(LvColor.White);
plot.FillRect(0, 119, 400, 1, LvColor.FromRgb(0xCCCCCCu));
plot.DrawLine(0, 200, 399, 40, LvColor.Blue);
plot.FillCircle(200, 120, 6, LvColor.Red);

Span<uint> pixels = plot.Pixels;                 // or write 0xAARRGGBB values directly
pixels[10 * plot.BufferWidth + 10] = 0xFFFF8800u;

plot.Invalidate();                               // once the frame is complete
```

The buffer lives in unmanaged memory owned by the canvas and is freed when LVGL deletes it, so
there is nothing to dispose. Drawing allocates nothing; `Invalidate()` tells LVGL to redraw on its
next refresh. The gallery's Mandelbrot, Visualizer, Photo lab and Snake demos are built on it.

## Scrolling

```csharp
log.ScrollToBottom(animate: true);      // the end of a log, chat or list
panel.ScrollTo(0, 120);                 // content offset
panel.ScrollBy(0, -40);                 // positive moves the content down
row.ScrollIntoView();                   // scroll every ancestor until this widget shows
var offset = panel.ScrollY;
```

## Symbols

```csharp
new LvButton(parent, $"{LvSymbols.Save} Save");
label.Text = $"{LvSymbols.Warning} Sensor offline";
```

`LvSymbols` exposes LVGL's built-in FontAwesome glyphs: `Ok`, `Close`, `Settings`, `Home`,
`Refresh`, `Play`, `Pause`, `Warning`, `Wifi`, `BatteryFull`, `Charge`, `Trash`, `Save`, and more.
