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
style only after every widget using it has gone.

## The widget set

| Class | LVGL widget | Notes |
|-------|-------------|-------|
| `LvPanel` | `lv_obj` | Plain container; the layout building block |
| `LvLabel` | `lv_label` | `Text`, `LongMode` |
| `LvButton` | `lv_button` | `Text` maintains a centred child label; `IsToggle` |
| `LvSlider` | `lv_slider` | `Value`, `SetRange`, `SetValue(v, animate)` |
| `LvBar` | `lv_bar` | Read-only progress |
| `LvArc` | `lv_arc` | Dial or rotary input; `IsReadOnly` |
| `LvSwitch` | `lv_switch` | `IsOn` |
| `LvCheckbox` | `lv_checkbox` | `Text`, `IsChecked` |
| `LvDropdown` | `lv_dropdown` | `Options`, `SelectedIndex`, `SelectedOption` |
| `LvRoller` | `lv_roller` | Touch-friendly picker |
| `LvTextArea` | `lv_textarea` | `Text`, `PlaceholderText`, `IsSingleLine` |
| `LvChart` | `lv_chart` | `AddSeries`, `PointCount`, `UpdateMode` |
| `LvScreen` | screen object | `Create()`, `Load()` for multi-page apps |

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
`LvState` values include `Pressed`, `Checked`, `Focused`, `Disabled`, `Hovered`.

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

Fonts are the built-in Montserrat sizes: 12, 14, 16, 20, 24, 28, 36. A size that was not compiled
into the native library falls back to the default rather than failing.

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

An exception thrown from a handler is swallowed — it cannot be allowed to unwind into LVGL's C
stack. Handle failures inside the handler.

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
cpu.Fill(0);                                     // prime, so it does not ramp in from zero

// later, once per sample
cpu.AddPoint(value);
```

LVGL keeps the ring buffer internally, so a sample costs one native call and allocates nothing.

## Symbols

```csharp
new LvButton(parent, $"{LvSymbols.Save} Save");
label.Text = $"{LvSymbols.Warning} Sensor offline";
```

`LvSymbols` exposes LVGL's built-in FontAwesome glyphs: `Ok`, `Close`, `Settings`, `Home`,
`Refresh`, `Play`, `Pause`, `Warning`, `Wifi`, `BatteryFull`, `Charge`, `Trash`, `Save`, and more.
