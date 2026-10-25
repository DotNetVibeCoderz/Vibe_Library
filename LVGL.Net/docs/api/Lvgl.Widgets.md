# Lvgl.Widgets

[API reference](README.md) · generated from the XML documentation; do not edit by hand.

## LvArc

`sealed class` in `LVGL.Net`

A circular arc, used both as a dial-style indicator and as a rotary input.

| Member | Description |
|--------|-------------|
| `LvArc(LvObject parent)` | Creates an arc on `parent`. |
| `bool IsReadOnly { get; set; }` | Makes the arc display-only by removing its click handling. |
| `int Rotation { set; }` | Rotates the whole arc, in degrees. |
| `int Value { get; set; }` | Current value. |
| `void SetBackgroundAngles(int startAngle, int endAngle)` | Sets the angular extent of the background track, in degrees clockwise from the 3 o'clock position. A gauge typically uses 135..45 together with [`Rotation`](#lvarc). |
| `void SetRange(int minimum, int maximum)` | Sets the accepted range. Defaults to 0..100. |

## LvBar

`sealed class` in `LVGL.Net`

A read-only progress bar.

| Member | Description |
|--------|-------------|
| `LvBar(LvObject parent)` | Creates a bar on `parent`. |
| `int Value { get; set; }` | Current value. Assigning animates the indicator. |
| `void SetRange(int minimum, int maximum)` | Sets the displayed range. Defaults to 0..100. |
| `void SetValue(int value, bool animate)` | Sets the value, optionally without animation. |

## LvButton

`sealed class` in `LVGL.Net`

A push button.

LVGL buttons hold no text of their own - the convention is a centred child label. [`Text`](#lvbutton)
creates and maintains that label so callers do not have to.

| Member | Description |
|--------|-------------|
| `LvButton(LvObject parent)` | Creates a button on `parent`. |
| `LvButton(LvObject parent, string text)` | Creates a button with a caption. |
| `bool IsToggle { get; set; }` | Makes the button latch between checked and unchecked instead of firing once per press. Read the result through [`IsChecked`](#lvobject). |
| `LvLabel Label { get; }` | The child label backing [`Text`](#lvbutton), or `null` if unused. |
| `string Text { get; set; }` | Caption text. Setting it creates a centred child label on first use. |

## LvChart

`sealed class` in `LVGL.Net`

A line, bar or scatter chart.

The realtime pattern this wrapper is built for is: create the chart once, set
[`UpdateMode`](#lvchart) to [`Shift`](Lvgl.md#lvchartupdatemode), then call
[`AddPoint`](#lvchartseries) as samples arrive. LVGL keeps the ring buffer internally,
so the managed side never allocates per sample.

| Member | Description |
|--------|-------------|
| `LvChart(LvObject parent)` | Creates a chart on `parent`. |
| `int PointCount { set; }` | Number of samples kept per series. Defaults to 10. |
| `IReadOnlyList<LvChartSeries> Series { get; }` | Series added to this chart. |
| `LvChartType Type { set; }` | Plot style. |
| `LvChartUpdateMode UpdateMode { set; }` | How new points are inserted. |
| `LvChartSeries AddSeries(LvColor color, LvChartAxis axis = LvChartAxis.PrimaryY)` | Adds a data series. |
| `void Refresh()` | Forces a redraw after bulk changes. |
| `void RemoveSeries(LvChartSeries series)` | Removes a series and its data. |
| `void SetDivisionLines(int horizontal, int vertical)` | Sets how many division lines the grid draws. |
| `void SetRange(LvChartAxis axis, int minimum, int maximum)` | Sets the value range of one axis. |

## LvChartSeries

`sealed class` in `LVGL.Net`

One data series inside an [`LvChart`](#lvchart).

| Member | Description |
|--------|-------------|
| `LvChartSeries(LvChart chart, nint handle, LvColor color)` | One data series inside an [`LvChart`](#lvchart). |
| `LvChart Chart { get; }` | The chart this series belongs to. |
| `LvColor Color { get; }` | Colour the series is drawn in. |
| `nint Handle { get; }` | Native `lv_chart_series_t*`. |
| `void AddPoint(int value)` | Appends a sample. With [`Shift`](Lvgl.md#lvchartupdatemode) the oldest point drops off, which is the behaviour wanted for a live telemetry trace. |
| `void Fill(int value)` | Sets every point of the series to one value - useful to prime a fresh chart. |
| `const int NoValue` | A sample with no value (`LV_CHART_POINT_NONE`): not drawn, and the line breaks there. [`Fill`](#lvchartseries) a fresh series with it so the trace starts where real data does. |

## LvCheckbox

`sealed class` in `LVGL.Net`

A checkbox with a caption.

| Member | Description |
|--------|-------------|
| `LvCheckbox(LvObject parent)` | Creates a checkbox on `parent`. |
| `LvCheckbox(LvObject parent, string text)` | Creates a checkbox with a caption. |
| `string Text { get; set; }` | Caption text. Cached on the managed side because LVGL exposes no getter that is safe to call after the widget has been deleted. |

## LvDropdown

`sealed class` in `LVGL.Net`

A drop-down list.

| Member | Description |
|--------|-------------|
| `LvDropdown(LvObject parent)` | Creates a drop-down on `parent`. |
| `LvDropdown(LvObject parent, params string[] options)` | Creates a drop-down with options. |
| `IReadOnlyList<string> Options { get; set; }` | The selectable entries. LVGL takes them as a single newline-separated string, so entries containing a newline are rejected rather than silently splitting into two rows. |
| `int SelectedIndex { get; set; }` | Index of the selected entry. |
| `string SelectedOption { get; }` | The selected entry, or an empty string when the list is empty. |

## LvImage

`sealed class` in `LVGL.Net`

Shows a picture: a PNG or BMP file, pixels produced at run time, or a built-in symbol.

The widget sizes itself to the source unless given an explicit size; with an explicit size,
[`InnerAlign`](#lvimage) decides whether the pixels are centred, stretched, tiled or fitted.
A pixel [`LvImageSource`](Lvgl.Drawing.md#lvimagesource) must outlive the widget - see its remarks.

| Member | Description |
|--------|-------------|
| `LvImage(LvObject parent)` | Creates an empty image on `parent`. |
| `LvImage(LvObject parent, LvImageSource source)` | Creates an image showing `source`. |
| `bool Antialias { set; }` | Smooths rotated and scaled images, at some rendering cost. |
| `LvImageAlign InnerAlign { set; }` | Placement of the pixels when the widget's size differs from the source's. |
| `int Rotation { get; set; }` | Rotation in tenths of a degree (900 is a quarter turn), about [`SetPivot`](#lvimage). |
| `int Scale { get; set; }` | Zoom where 256 is 100 %, 512 is 200 % and 128 is 50 %. |
| `LvImageSource Source { get; set; }` | The source being shown, or null for none or for a symbol. |
| `int SourceHeight { get; }` | Height of the source in pixels, once LVGL has read it. |
| `int SourceWidth { get; }` | Width of the source in pixels, once LVGL has read it. |
| `void SetFile(string path)` | Shows a file directly. Equivalent to `Source = LvImageSource.FromFile(path)`. |
| `void SetPivot(int x, int y)` | Sets the point rotation and zoom are applied around, relative to the image. |
| `void SetSymbol(string symbol)` | Shows a built-in symbol glyph from [`LvSymbols`](Lvgl.md#lvsymbols), drawn in the text colour. |

## LvKeyboard

`sealed class` in `LVGL.Net`

An on-screen keyboard that types into an [`LvTextArea`](#lvtextarea) - text entry on a touch
panel with no physical keyboard.

A common pattern is one keyboard per screen, hidden until a text area is focused:




```csharp
var keyboard = new LvKeyboard(screen) { IsHidden = true };
field.AddHandler(LvEventCode.Focused, (_, _) => { keyboard.TextArea = field; keyboard.IsHidden = false; });
keyboard.AddHandler(LvEventCode.Ready, (_, _) => keyboard.IsHidden = true);
```




The keyboard raises [`Ready`](Lvgl.md#lveventcode) for its OK key and
[`Cancel`](Lvgl.md#lveventcode) for its close key.

| Member | Description |
|--------|-------------|
| `LvKeyboard(LvObject parent)` | Creates a keyboard on `parent`, docked to the bottom by LVGL's theme. |
| `LvKeyboardMode Mode { get; set; }` | Key layout: letters, symbols or digits. |
| `bool ShowPopovers { set; }` | Shows an enlarged key above the finger while pressing - helpful on small panels. |
| `LvTextArea TextArea { get; set; }` | The field the keys type into; null detaches the keyboard. |

## LvLabel

`sealed class` in `LVGL.Net`

A text label.

| Member | Description |
|--------|-------------|
| `LvLabel(LvObject parent)` | Creates a label on `parent`. |
| `LvLabel(LvObject parent, string text)` | Creates a label with initial text. |
| `LvLabelLongMode LongMode { set; }` | How text that does not fit the label's width is handled. |
| `string Text { get; set; }` | The displayed text. LVGL copies the string into its own buffer on assignment, so the managed string does not need to be kept alive. |

## LvLed

`sealed class` in `LVGL.Net`

A round status light with adjustable colour and brightness.

| Member | Description |
|--------|-------------|
| `LvLed(LvObject parent)` | Creates an LED on `parent`. |
| `byte Brightness { get; set; }` | Brightness from 0 to 255. LVGL treats roughly 80 and below as "off" and draws the glow only above that. |
| `LvColor Color { get; set; }` | Light colour. |
| `void Off()` | Minimum brightness. |
| `void On()` | Full brightness. |
| `void Toggle()` | Flips between on and off. |

## LvList

`sealed class` in `LVGL.Net`

A vertical, scrollable menu of buttons with optional section headings - the usual settings
screen on a touch panel.

| Member | Description |
|--------|-------------|
| `LvList(LvObject parent)` | Creates a list on `parent`. |
| `LvListButton AddButton(string text, string symbol = null)` | Adds a button, optionally with a leading [`LvSymbols`](Lvgl.md#lvsymbols) icon. |
| `LvLabel AddText(string text)` | Adds a section heading. |

## LvListButton

`sealed class` in `LVGL.Net`

A button inside an [`LvList`](#lvlist).

| Member | Description |
|--------|-------------|
| `string Text { get; set; }` | The caption. |

## LvMessageBox

`sealed class` in `LVGL.Net`

A dialog with a title, text and buttons.

[`Show`](#lvmessagebox) creates a **modal** box: LVGL puts it on the top layer behind a dimmed
backdrop that swallows clicks, and the box closes itself (deleting itself and the backdrop)
when [`Close`](#lvmessagebox) is called or its close button is pressed.





Message boxes are runtime UI, not layout: they are created in response to something
happening, so the designer and layout documents do not include them.

| Member | Description |
|--------|-------------|
| `LvMessageBox(LvObject parent)` | Creates a non-modal message box inside `parent`. |
| `IReadOnlyList<LvObject> Buttons { get; }` | Footer buttons in the order they were added. |
| `LvObject Content { get; }` | The body container, for adding custom widgets. |
| `LvObject AddButton(string caption)` | Adds a footer button. |
| `LvObject AddCloseButton()` | Adds the "X" button in the header that closes the box. |
| `LvLabel AddText(string text)` | Adds a paragraph to the body. |
| `LvLabel AddTitle(string title)` | Sets the heading. |
| `void Close()` | Closes the box, deleting it (and its modal backdrop, if any). |
| `static LvMessageBox Show(string title, string text, params string[] buttons)` | Shows a modal message box with the given footer buttons.<br>`title`: Heading; null or empty for none.<br>`text`: Body text.<br>`buttons`: Footer button captions, left to right. |

## LvObject

`class` in `LVGL.Net`

Managed wrapper around an `lv_obj_t` - the base of every LVGL widget.

**Lifetime.** LVGL owns the object tree: deleting a parent deletes its children. This class
therefore does not implement `IDisposable`, because "disposing" a child that its
parent already freed would be a double free. Call [`Delete`](#lvobject) when you genuinely want
the widget gone; otherwise let the screen or parent take it with them.




**Event dispatch.** Managed instances are found from a native pointer through a static
registry rather than a `GCHandle` stored in the object's user data. LVGL can fire
callbacks while it is tearing an object down, and a freed `GCHandle` read during
that window is undefined behaviour, whereas a missed dictionary lookup is merely a no-op.
Entries are removed when LVGL reports the delete event.

| Member | Description |
|--------|-------------|
| `LvObject(LvObject parent)` | Creates a plain container object.<br>`parent`: Parent widget; `null` parents to the active screen. |
| `int ChildCount { get; }` | Number of direct children. |
| `nint Handle { get; }` | Native `lv_obj_t*`. |
| `int Height { get; set; }` | Height in pixels. Accepts [`Percent`](Lvgl.Drawing.md#lvcoord) and [`SizeContent`](Lvgl.Drawing.md#lvcoord). |
| `bool IsAlive { get; }` | False once LVGL has freed the underlying object. |
| `bool IsChecked { get; set; }` | Whether the widget is in the checked state (toggles, switches, checkboxes). |
| `bool IsClickable { get; set; }` | Whether the widget reacts to pointer input. |
| `bool IsDisabled { get; set; }` | Whether the widget is in the disabled state. |
| `bool IsHidden { get; set; }` | Hides the widget without removing it from the tree. |
| `bool IsScrollable { get; set; }` | Whether the widget scrolls its content. |
| `string Name { get; set; }` | Optional identifier used by the designer and code generator. Not sent to LVGL. |
| `LvObject Parent { get; set; }` | Parent widget, or `null` for a screen. |
| `int Width { get; set; }` | Width in pixels. Accepts [`Percent`](Lvgl.Drawing.md#lvcoord) and [`SizeContent`](Lvgl.Drawing.md#lvcoord). |
| `int X { get; set; }` | X position relative to the parent's content area. |
| `int Y { get; set; }` | Y position relative to the parent's content area. |
| `int ZIndex { get; set; }` | Position among its siblings; higher indices are drawn on top. Setting it re-orders the widget, so 0 sends it to the back. |
| `void AddHandler(LvEventCode code, EventHandler<LvEventArgs> handler)` | Subscribes to any LVGL event code. |
| `LvObject AddStyle(LvStyle style, LvPart part = LvPart.Main, LvState state = LvState.Default)` | Applies a shared [`LvStyle`](Lvgl.Styling.md#lvstyle). |
| `LvObject Align(LvAlign align, int xOffset = 0, int yOffset = 0)` | Aligns inside the parent, with an optional offset. |
| `LvObject AlignTo(LvObject reference, LvAlign align, int xOffset = 0, int yOffset = 0)` | Aligns relative to another object. |
| `LvObject Center()` | Centres inside the parent. |
| `IEnumerable<LvObject> Children()` | Enumerates children that have a managed wrapper. |
| `void Clear()` | Deletes all children, keeping this object. |
| `void Delete()` | Deletes the object and its children. The instance becomes unusable; [`IsAlive`](#lvobject) turns false once LVGL confirms the deletion. |
| `static LvObject FromHandle(nint handle)` | Returns the managed wrapper for a native handle, if one exists. |
| `LvArea GetBounds()` | Absolute bounds on the display, after layout and scrolling - what the user actually sees. |
| `LvObject GetChild(int index)` | Child at `index`. Returns `null` when the child was created by native code and has no managed wrapper (for example widgets built by an LVGL demo). |
| `bool HasFlag(LvObjFlag flag)` |  |
| `bool HasState(LvState state)` |  |
| `void Invalidate()` | Marks the widget for redraw. |
| `void RemoveHandler(LvEventCode code, EventHandler<LvEventArgs> handler)` | Unsubscribes a previously added handler. |
| `LvObject RemoveStyle(LvStyle style, LvPart part = LvPart.Main, LvState state = LvState.Default)` | Removes a previously applied shared style. |
| `static uint Selector(LvPart part = LvPart.Main, LvState state = LvState.Default)` | Combines a part and a state into the selector LVGL's style setters take. |
| `LvObject SetArcColor(LvColor color, LvPart part = LvPart.Main, LvState state = LvState.Default)` |  |
| `LvObject SetArcWidth(int width, LvPart part = LvPart.Main, LvState state = LvState.Default)` |  |
| `LvObject SetBackgroundColor(LvColor color, LvPart part = LvPart.Main, LvState state = LvState.Default)` |  |
| `LvObject SetBackgroundGradientColor(LvColor color, LvPart part = LvPart.Main, LvState state = LvState.Default)` | Sets a vertical gradient's end colour. The start colour is the background colour. |
| `LvObject SetBackgroundOpacity(byte opacity, LvPart part = LvPart.Main, LvState state = LvState.Default)` | Background opacity, 0 (transparent) to 255 (opaque). |
| `LvObject SetBorderColor(LvColor color, LvPart part = LvPart.Main, LvState state = LvState.Default)` |  |
| `LvObject SetBorderWidth(int width, LvPart part = LvPart.Main, LvState state = LvState.Default)` |  |
| `LvObject SetFlag(LvObjFlag flag, bool enabled)` |  |
| `LvObject SetFlexAlign(LvFlexAlign main, LvFlexAlign cross, LvFlexAlign track)` | Sets how flex children are distributed. |
| `LvObject SetFlexFlow(LvFlexFlow flow)` | Turns the widget into a flex container. |
| `LvObject SetFont(LvFont font, LvPart part = LvPart.Main, LvState state = LvState.Default)` | Applies a font. A TrueType [`LvFont`](Lvgl.Drawing.md#lvfont) must outlive the widget, exactly like an [`LvStyle`](Lvgl.Styling.md#lvstyle). |
| `LvObject SetFontSize(int size, LvPart part = LvPart.Main, LvState state = LvState.Default)` | Selects one of the built-in Montserrat fonts. Sizes not compiled into the native library fall back to the default font rather than failing. |
| `LvObject SetGap(int rowGap, int columnGap, LvPart part = LvPart.Main, LvState state = LvState.Default)` | Gap between rows and columns of a flex or grid layout. |
| `LvObject SetImageRecolor(LvColor color, byte opacity = 255, LvPart part = LvPart.Main, LvState state = LvState.Default)` | Tints an image towards `color` by `opacity` (0-255). |
| `LvObject SetLineColor(LvColor color, LvPart part = LvPart.Main, LvState state = LvState.Default)` |  |
| `LvObject SetLineWidth(int width, LvPart part = LvPart.Main, LvState state = LvState.Default)` |  |
| `LvObject SetOpacity(byte opacity, LvPart part = LvPart.Main, LvState state = LvState.Default)` | Opacity of the whole widget, 0 to 255. |
| `LvObject SetPadding(int padding, LvPart part = LvPart.Main, LvState state = LvState.Default)` |  |
| `LvObject SetPartSize(int width, int height, LvPart part, LvState state = LvState.Default)` | Size of a part the widget draws itself - a chart's point markers ([`Indicator`](Lvgl.md#lvpart)), a slider's knob. Zero hides chart point markers, leaving a plain line. |
| `LvObject SetPosition(int x, int y)` | Sets position in one call. |
| `LvObject SetRadius(int radius, LvPart part = LvPart.Main, LvState state = LvState.Default)` |  |
| `LvObject SetScrollbarMode(LvScrollbarMode mode)` | Sets scrollbar visibility. |
| `LvObject SetShadowColor(LvColor color, LvPart part = LvPart.Main, LvState state = LvState.Default)` |  |
| `LvObject SetSize(int width, int height)` | Sets size in one call. |
| `LvObject SetState(LvState state, bool enabled)` |  |
| `LvObject SetTextAlign(LvTextAlign align, LvPart part = LvPart.Main, LvState state = LvState.Default)` |  |
| `LvObject SetTextColor(LvColor color, LvPart part = LvPart.Main, LvState state = LvState.Default)` |  |
| `string ToString()` |  |
| `void UpdateLayout()` | Resolves pending layout immediately. |
| `event EventHandler<LvEventArgs> Clicked` | Raised when the widget is clicked. |
| `event EventHandler<LvEventArgs> Deleted` | Raised when LVGL deletes the widget. |
| `event EventHandler<LvEventArgs> ValueChanged` | Raised when the widget's value changes (slider dragged, switch toggled, ...). |

## LvPanel

`sealed class` in `LVGL.Net`

A plain container. Identical to a bare [`LvObject`](#lvobject), named for readability when it is
used purely as a grouping or layout element.

| Member | Description |
|--------|-------------|
| `LvPanel(LvObject parent)` | Creates a container on `parent`. |
| `LvPanel(LvObject parent, int width, int height)` | Creates a container with a size. |

## LvRoller

`sealed class` in `LVGL.Net`

A scrolling picker, well suited to touch input.

| Member | Description |
|--------|-------------|
| `LvRoller(LvObject parent)` | Creates a roller on `parent`. |
| `LvRoller(LvObject parent, params string[] options)` | Creates a roller with options. |
| `LvRollerMode Mode { get; set; }` | Whether the list wraps around when scrolled past its ends. |
| `IReadOnlyList<string> Options { get; set; }` | The selectable entries. |
| `int SelectedIndex { get; }` | Index of the selected entry. |
| `string SelectedOption { get; }` | The selected entry, or an empty string when the list is empty. |

## LvScale

`sealed class` in `LVGL.Net`

A ruler or gauge face: ticks and labels along a line or an arc. LVGL v9's replacement for the
v8 meter.

Pair a round scale with an [`LvArc`](#lvarc) or a needle drawn on top to build a gauge; the
scale itself shows no value.

| Member | Description |
|--------|-------------|
| `LvScale(LvObject parent)` | Creates a scale on `parent`. |
| `int AngleRange { set; }` | Sweep of a round scale in degrees (270 is a typical gauge). |
| `int MajorTickEvery { get; set; }` | Every n-th tick is major (longer, and labelled when [`ShowLabels`](#lvscale) is on). |
| `int Maximum { get; }` | Value of the last tick. |
| `int Minimum { get; }` | Value of the first tick. |
| `LvScaleMode Mode { get; set; }` | Shape and tick side. |
| `int Rotation { set; }` | Where a round scale starts, in degrees clockwise from 3 o'clock. |
| `bool ShowLabels { set; }` | Whether major ticks carry their value as a label. |
| `int TotalTickCount { get; set; }` | Number of ticks, major and minor together. |
| `void SetRange(int minimum, int maximum)` | Sets the values the first and last tick stand for. |

## LvScreen

`sealed class` in `LVGL.Net`

A root object. Exactly one screen is displayed at a time; [`Load`](#lvscreen) switches between
them, which is the usual way to build multi-page applications.

| Member | Description |
|--------|-------------|
| `static LvScreen Active()` | Wraps the screen LVGL is currently showing. |
| `static LvScreen Create()` | Creates a new, empty screen that is not displayed until [`Load`](#lvscreen) is called. |
| `void Load()` | Makes this screen the displayed one. |

## LvSlider

`sealed class` in `LVGL.Net`

A draggable slider.

| Member | Description |
|--------|-------------|
| `LvSlider(LvObject parent)` | Creates a slider on `parent`. |
| `int Value { get; set; }` | Current value. Assigning animates by default; use [`SetValue`](#lvslider) to control that. |
| `void SetRange(int minimum, int maximum)` | Sets the accepted range. Defaults to 0..100. |
| `void SetValue(int value, bool animate)` | Sets the value, optionally without animation. |

## LvSpinbox

`sealed class` in `LVGL.Net`

A numeric field stepped digit by digit - set points, thresholds, calibration values.

The value is an integer; [`SetDigitFormat`](#lvspinbox) places a decimal point for display, so
2150 with one decimal place reads "215.0". Pair it with two buttons calling
[`Increment`](#lvspinbox) and [`Decrement`](#lvspinbox) for touch input.

| Member | Description |
|--------|-------------|
| `LvSpinbox(LvObject parent)` | Creates a spinbox on `parent`. |
| `bool Rollover { set; }` | Wraps from maximum to minimum and back instead of stopping. |
| `int Step { get; set; }` | Amount [`Increment`](#lvspinbox) and [`Decrement`](#lvspinbox) change the value by. |
| `int Value { get; set; }` | Current value, clamped by LVGL to the range. |
| `void Decrement()` | Subtracts [`Step`](#lvspinbox). |
| `void Increment()` | Adds [`Step`](#lvspinbox). |
| `void SetDigitFormat(int digits, int decimalPlaces = 0)` | Sets how many digits are shown and where the decimal point goes.<br>`digits`: Total digits, 1 to 10.<br>`decimalPlaces`: Digits after the decimal point; 0 for an integer display. |
| `void SetRange(int minimum, int maximum)` | Sets the accepted range. |

## LvSpinner

`sealed class` in `LVGL.Net`

A rotating arc that says "working on it".

The spinner animates itself on LVGL's timer, so it costs redraws for as long as it is visible:
hide or delete it when the work finishes rather than leaving it spinning off-screen.

| Member | Description |
|--------|-------------|
| `LvSpinner(LvObject parent)` | Creates a spinner on `parent`. |
| `int ArcSweep { get; }` | Length of the moving arc in degrees. |
| `int Duration { get; }` | Milliseconds per revolution. |
| `void SetAnimation(int durationMs, int arcSweepDegrees)` | Sets the time for one revolution and the length of the moving arc in degrees. |

## LvSwitch

`sealed class` in `LVGL.Net`

An on/off switch.

| Member | Description |
|--------|-------------|
| `LvSwitch(LvObject parent)` | Creates a switch on `parent`. |
| `bool IsOn { get; set; }` | Whether the switch is on. Backed by LVGL's checked state, so it can also be styled through [`Checked`](Lvgl.md#lvstate). |

## LvTabPage

`sealed class` in `LVGL.Net`

One page of an [`LvTabView`](#lvtabview); an ordinary container for the tab's widgets.

| Member | Description |
|--------|-------------|
| `int Index { get; }` | Position of the tab. |
| `LvTabView Owner { get; }` | The tab view this page belongs to. |
| `void Activate(bool animate = false)` | Brings this page to the front. |

## LvTabView

`sealed class` in `LVGL.Net`

Pages behind a row of tabs. Each [`AddTab`](#lvtabview) returns the page to put widgets on.

[`ValueChanged`](#lvobject) fires when the user switches tab; read
[`ActiveTab`](#lvtabview) in the handler.

| Member | Description |
|--------|-------------|
| `LvTabView(LvObject parent)` | Creates a tab view on `parent`. |
| `int ActiveTab { get; set; }` | Index of the visible tab. Assigning switches without animation. |
| `LvObject TabBar { get; }` | The bar holding the tab buttons, for styling. |
| `LvDir TabBarPosition { set; }` | Which edge the tab bar sits on: [`Top`](Lvgl.md#lvdir) (default), Bottom, Left or Right. |
| `int TabBarSize { set; }` | Height of a horizontal tab bar, or width of a vertical one, in pixels. |
| `int TabCount { get; }` | Number of tabs. |
| `IReadOnlyList<LvTabPage> Tabs { get; }` | The pages, in tab order. |
| `LvTabPage AddTab(string title)` | Adds a tab and returns its page. |
| `void SetActiveTab(int index, bool animate)` | Switches tab, optionally sliding the content across. |
| `void SetTabTitle(int index, string title)` | Renames a tab. |

## LvTable

`sealed class` in `LVGL.Net`

A grid of text cells.

LVGL draws the whole table as one widget rather than a widget per cell, so it stays cheap at
hundreds of cells. Writing a cell beyond the current size grows the table.

| Member | Description |
|--------|-------------|
| `LvTable(LvObject parent)` | Creates a table on `parent`. |
| `int ColumnCount { get; set; }` | Number of columns. |
| `string this[int row, int column] { get; set; }` | Gets or sets the text of a cell. |
| `int RowCount { get; set; }` | Number of rows. |
| `int GetColumnWidth(int column)` | A column's width in pixels. |
| `void SetColumnWidth(int column, int width)` | Sets a column's width in pixels. |
| `void SetRows(IReadOnlyList<IReadOnlyList<string>> rows)` | Replaces the contents with `rows`, sizing the table to fit the widest row. |

## LvTextArea

`sealed class` in `LVGL.Net`

An editable text field.

| Member | Description |
|--------|-------------|
| `LvTextArea(LvObject parent)` | Creates a text area on `parent`. |
| `bool IsSingleLine { set; }` | Restricts the field to a single line, suppressing the Enter key. |
| `string PlaceholderText { set; }` | Hint shown while the field is empty. |
| `string Text { get; set; }` | The current contents. |

## LvTile

`sealed class` in `LVGL.Net`

One page of an [`LvTileView`](#lvtileview); an ordinary container for its widgets.

| Member | Description |
|--------|-------------|
| `int Column { get; }` | Grid column. |
| `int Row { get; }` | Grid row. |

## LvTileView

`sealed class` in `LVGL.Net`

Full-size pages laid out on a grid that the user swipes between - a phone-style home screen.

Each tile states which directions it may be left in, so a grid with gaps or one-way paths can
be built. [`ValueChanged`](#lvobject) fires after a swipe settles on a new tile.

| Member | Description |
|--------|-------------|
| `LvTileView(LvObject parent)` | Creates a tile view on `parent`. |
| `LvTile ActiveTile { get; }` | The tile currently in view. Null until LVGL has settled on one - after [`ShowTile`](#lvtileview) or the first swipe - because LVGL does not mark the first tile active when it is added. |
| `IReadOnlyList<LvTile> Tiles { get; }` | The tiles in the order they were added. |
| `LvTile AddTile(int column, int row, LvDir allowedDirections = LvDir.All)` | Adds a tile at a grid position.<br>`column`: Column, from 0.<br>`row`: Row, from 0.<br>`allowedDirections`: Directions a swipe may leave this tile in. |
| `void ShowTile(int column, int row, bool animate = false)` | Scrolls to the tile at a grid position. |

