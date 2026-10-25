# Lvgl

[API reference](README.md) · generated from the XML documentation; do not edit by hand.

## LvAlign

`enum` in `LVGL.Net`

Alignment of an object inside its parent (or relative to another object).

| Value | Description |
|--------|-------------|
| `Default = 0` |  |
| `TopLeft = 1` |  |
| `TopMid = 2` |  |
| `TopRight = 3` |  |
| `BottomLeft = 4` |  |
| `BottomMid = 5` |  |
| `BottomRight = 6` |  |
| `LeftMid = 7` |  |
| `RightMid = 8` |  |
| `Center = 9` |  |
| `OutTopLeft = 10` |  |
| `OutTopMid = 11` |  |
| `OutTopRight = 12` |  |
| `OutBottomLeft = 13` |  |
| `OutBottomMid = 14` |  |
| `OutBottomRight = 15` |  |
| `OutLeftTop = 16` |  |
| `OutLeftMid = 17` |  |
| `OutLeftBottom = 18` |  |
| `OutRightTop = 19` |  |
| `OutRightMid = 20` |  |
| `OutRightBottom = 21` |  |

## LvAnimPath

`enum` in `LVGL.Net`

Easing curve of an animation. These are LVGL.Net's own identifiers, mapped onto LVGL's path
functions in the shim, so they are stable across LVGL releases.

| Value | Description |
|--------|-------------|
| `Linear = 0` |  |
| `EaseIn = 1` |  |
| `EaseOut = 2` |  |
| `EaseInOut = 3` |  |
| `Overshoot = 4` |  |
| `Bounce = 5` |  |
| `Step = 6` | Jumps from the start value to the end value at the end of the duration. |

## LvAnimation

`sealed class` in `LVGL.Net`

Animates an integer from one value to another over time, driven by LVGL's own animation timer.

Each frame LVGL computes the eased value and calls [`Apply`](#lvanimation) with it, so anything an
integer can drive can be animated: a position, an opacity, a bar's value, a colour component.
The `Animate*` helpers on [`LvAnimations`](#lvanimations) cover the common widget properties.




**Lifetime.** A running animation is kept alive by LVGL.Net, so it does not need to be held
in a field. When [`Target`](#lvanimation) is set, deleting that widget stops the animation - without
it, the next frame would call [`Apply`](#lvanimation) on a deleted widget.




**Routing.** LVGL identifies the animation by an opaque id rather than a widget pointer, and
the callbacks find the instance through a registry - the same reasoning as widget events: a
stale id is a missed lookup, never a read of freed memory. Stopping one animation therefore
never stops another running on the same widget.

```csharp
new LvAnimation(card, x => card.X = x, from: -200, to: 16)
{
    Duration = 400,
    Path = LvAnimPath.EaseOut,
}.Start();
```

| Member | Description |
|--------|-------------|
| `LvAnimation(LvObject target, Action<int> apply, int from, int to)` | Creates an animation; nothing moves until [`Start`](#lvanimation).<br>`target`: Widget the animation belongs to; it stops when the widget is deleted. May be null.<br>`apply`: Receives each frame's value. Runs on the UI thread, once per frame - keep it cheap.<br>`from`: Start value.<br>`to`: End value. |
| `Action<int> Apply { get; }` | Receives each frame's value. |
| `bool ApplyFromImmediately { get; set; }` | Applies [`From`](#lvanimation) immediately on [`Start`](#lvanimation) even when there is a [`Delay`](#lvanimation), so the widget does not jump when the delay ends. Default true. |
| `int Delay { get; set; }` | Milliseconds to wait before starting. |
| `int Duration { get; set; }` | Milliseconds from [`From`](#lvanimation) to [`To`](#lvanimation). Default 300. |
| `int From { get; set; }` | Start value. |
| `bool IsRunning { get; }` | True between [`Start`](#lvanimation) and the animation finishing or being stopped. |
| `LvAnimPath Path { get; set; }` | Easing curve. Default [`EaseInOut`](#lvanimpath). |
| `int RepeatCount { get; set; }` | Total number of runs; 1 (the default) plays once, [`Infinite`](#lvanimation) forever. |
| `int RepeatDelay { get; set; }` | Milliseconds between repeats. |
| `int ReverseDelay { get; set; }` | Milliseconds to pause at [`To`](#lvanimation) before playing back. |
| `int ReverseDuration { get; set; }` | When greater than zero, the animation plays back from [`To`](#lvanimation) to [`From`](#lvanimation) over this many milliseconds after each forward run. |
| `int RunningCount { get; }` | Number of animations currently running through LVGL.Net. |
| `LvObject Target { get; }` | The widget the animation belongs to, if any. |
| `int To { get; set; }` | End value. |
| `LvAnimation Start()` | Starts (or restarts) the animation.<br>Returns: This instance, for chaining. |
| `void Stop()` | Stops the animation where it is. Does nothing when it is not running. |
| `const int Infinite` | Pass as [`RepeatCount`](#lvanimation) to repeat forever. |
| `event EventHandler Completed` | Raised when the animation finishes on its own; not raised by [`Stop`](#lvanimation). |

## LvAnimations

`static class` in `LVGL.Net`

Shortcuts for animating common widget properties.

| Member | Description |
|--------|-------------|
| `static LvAnimation AnimateHeight(LvObject widget, int to, int durationMs = 300, LvAnimPath path = LvAnimPath.EaseInOut)` | Resizes the widget's height from its current value to `to`. |
| `static LvAnimation AnimateOpacity(LvObject widget, byte from, byte to, int durationMs = 300, LvAnimPath path = LvAnimPath.EaseInOut)` | Fades the whole widget between two opacities (0-255). |
| `static LvAnimation AnimateValue(LvArc arc, int to, int durationMs = 300, LvAnimPath path = LvAnimPath.EaseInOut)` | Animates an arc's value from its current value to `to`. |
| `static LvAnimation AnimateValue(LvBar bar, int to, int durationMs = 300, LvAnimPath path = LvAnimPath.EaseInOut)` | Animates a bar's value from its current value to `to`. |
| `static LvAnimation AnimateWidth(LvObject widget, int to, int durationMs = 300, LvAnimPath path = LvAnimPath.EaseInOut)` | Resizes the widget's width from its current value to `to`. |
| `static LvAnimation AnimateX(LvObject widget, int to, int durationMs = 300, LvAnimPath path = LvAnimPath.EaseInOut)` | Slides the widget horizontally from its current X to `to`. |
| `static LvAnimation AnimateY(LvObject widget, int to, int durationMs = 300, LvAnimPath path = LvAnimPath.EaseInOut)` | Slides the widget vertically from its current Y to `to`. |

## LvChartAxis

`enum` in `LVGL.Net`

Axis a chart series is bound to.

| Value | Description |
|--------|-------------|
| `PrimaryY = 0` |  |
| `SecondaryY = 1` |  |
| `PrimaryX = 2` |  |
| `SecondaryX = 4` |  |

## LvChartType

`enum` in `LVGL.Net`

Chart plot style.

`Curve` and `Stacked` were inserted mid-enum after LVGL 9.2, moving `Bar` and
`Scatter`. The values are checked against the native build by the integration tests.
Saved layouts store the name, not the number, so the move does not affect them.

| Value | Description |
|--------|-------------|
| `None = 0` |  |
| `Line = 1` |  |
| `Curve = 2` | A line smoothed into a curve. |
| `Bar = 3` |  |
| `Stacked = 4` | Bars of every series stacked on top of each other. |
| `Scatter = 5` |  |

## LvChartUpdateMode

`enum` in `LVGL.Net`

How new chart points are inserted.

| Value | Description |
|--------|-------------|
| `Shift = 0` | Old points shift left as new ones arrive - the usual choice for live telemetry. |
| `Circular = 1` | New points overwrite the oldest in place, like an oscilloscope sweep. |

## LvDir

`enum` in `LVGL.Net`

A direction, or a set of them (tab bar side, allowed tile scroll directions).

| Value | Description |
|--------|-------------|
| `None = 0` |  |
| `Left = 1` |  |
| `Right = 2` |  |
| `Horizontal = 3` |  |
| `Top = 4` |  |
| `Bottom = 8` |  |
| `Vertical = 12` |  |
| `All = 15` |  |

## LvEventCode

`enum` in `LVGL.Net`

Event kinds. These are LVGL.Net's own stable identifiers; the native code translates them into
this LVGL build's `lv_event_code_t`, whose ordinals differ between point releases.

| Value | Description |
|--------|-------------|
| `All = 0` |  |
| `Pressed = 1` |  |
| `Pressing = 2` |  |
| `PressLost = 3` |  |
| `ShortClicked = 4` |  |
| `LongPressed = 5` |  |
| `LongPressedRepeat = 6` |  |
| `Clicked = 7` |  |
| `Released = 8` |  |
| `ScrollBegin = 9` |  |
| `ScrollEnd = 10` |  |
| `Scroll = 11` |  |
| `Gesture = 12` |  |
| `Key = 13` |  |
| `Focused = 14` |  |
| `Defocused = 15` |  |
| `Leave = 16` |  |
| `ValueChanged = 17` |  |
| `Insert = 18` |  |
| `Refresh = 19` |  |
| `Ready = 20` |  |
| `Cancel = 21` |  |
| `Delete = 22` |  |
| `ChildChanged = 23` |  |
| `SizeChanged = 24` |  |
| `StyleChanged = 25` |  |
| `ScreenLoaded = 26` |  |
| `ScreenUnloaded = 27` |  |

## LvFlexAlign

`enum` in `LVGL.Net`

Distribution of items along a flex axis.

| Value | Description |
|--------|-------------|
| `Start = 0` |  |
| `End = 1` |  |
| `Center = 2` |  |
| `SpaceEvenly = 3` |  |
| `SpaceAround = 4` |  |
| `SpaceBetween = 5` |  |

## LvFlexFlow

`enum` in `LVGL.Net`

Direction and wrapping of a flex layout.

| Value | Description |
|--------|-------------|
| `Row = 0` |  |
| `Column = 1` |  |
| `RowWrap = 4` |  |
| `ColumnWrap = 5` |  |
| `RowReverse = 8` |  |
| `ColumnReverse = 9` |  |
| `RowWrapReverse = 12` |  |
| `ColumnWrapReverse = 13` |  |

## LvImageAlign

`enum` in `LVGL.Net`

How an image's pixels are placed inside the widget when the sizes differ.

| Value | Description |
|--------|-------------|
| `Default = 0` |  |
| `TopLeft = 1` |  |
| `TopMid = 2` |  |
| `TopRight = 3` |  |
| `BottomLeft = 4` |  |
| `BottomMid = 5` |  |
| `BottomRight = 6` |  |
| `LeftMid = 7` |  |
| `RightMid = 8` |  |
| `Center = 9` |  |
| `Stretch = 11` |  |
| `Tile = 12` |  |
| `Contain = 13` |  |
| `ContainDownscale = 14` |  |
| `Cover = 15` |  |

## LvIndevType

`enum` in `LVGL.Net`

Kind of input device fed into LVGL.

| Value | Description |
|--------|-------------|
| `None = 0` |  |
| `Pointer = 1` |  |
| `Keypad = 2` |  |
| `Button = 3` |  |
| `Encoder = 4` |  |

## LvKeyboardMode

`enum` in `LVGL.Net`

Key layout shown by an on-screen keyboard.

| Value | Description |
|--------|-------------|
| `TextLower = 0` |  |
| `TextUpper = 1` |  |
| `Special = 2` |  |
| `Number = 3` |  |

## LvLabelLongMode

`enum` in `LVGL.Net`

How a label handles text that does not fit.

| Value | Description |
|--------|-------------|
| `Wrap = 0` |  |
| `Dot = 1` |  |
| `Scroll = 2` |  |
| `ScrollCircular = 3` |  |
| `Clip = 4` |  |

## LvObjFlag

`enum` in `LVGL.Net`

Per-object behaviour switches.

| Value | Description |
|--------|-------------|
| `Hidden = 1` |  |
| `Clickable = 2` |  |
| `ClickFocusable = 4` |  |
| `Checkable = 8` |  |
| `Scrollable = 16` |  |
| `ScrollElastic = 32` |  |
| `ScrollMomentum = 64` |  |
| `ScrollOne = 128` |  |
| `ScrollChainHorizontal = 256` |  |
| `ScrollChainVertical = 512` |  |
| `ScrollOnFocus = 1024` |  |
| `ScrollWithArrow = 2048` |  |
| `Snappable = 4096` |  |
| `PressLock = 8192` |  |
| `EventBubble = 16384` |  |
| `GestureBubble = 32768` |  |
| `AdvancedHitTest = 65536` |  |
| `IgnoreLayout = 131072` |  |
| `Floating = 262144` |  |
| `OverflowVisible = 1048576` |  |

## LvPart

`enum` in `LVGL.Net`

The visual part of a widget a style applies to. Combine with [`LvState`](#lvstate) to form the
style selector, e.g. `LvPart.Knob | (uint)LvState.Pressed`.

| Value | Description |
|--------|-------------|
| `Main = 0` |  |
| `Scrollbar = 65536` |  |
| `Indicator = 131072` |  |
| `Knob = 196608` |  |
| `Selected = 262144` |  |
| `Items = 327680` |  |
| `Cursor = 393216` |  |
| `Any = 983040` |  |

## LvRenderMode

`enum` in `LVGL.Net`

How LVGL hands rendered pixels to the backend.

| Value | Description |
|--------|-------------|
| `Partial = 0` | Render into a small buffer and flush it in horizontal slices. Lowest memory use. |
| `Direct = 1` | Render into a full-screen buffer; only changed areas are redrawn. |
| `Full = 2` | Redraw the whole screen into a full-screen buffer every frame. |

## LvRollerMode

`enum` in `LVGL.Net`

Roller scrolling behaviour.

| Value | Description |
|--------|-------------|
| `Normal = 0` |  |
| `Infinite = 1` |  |

## LvScaleMode

`enum` in `LVGL.Net`

Shape and tick side of a scale.

| Value | Description |
|--------|-------------|
| `HorizontalTop = 0` |  |
| `HorizontalBottom = 1` |  |
| `VerticalLeft = 2` |  |
| `VerticalRight = 4` |  |
| `RoundInner = 8` | Round scale with the labels inside the ticks - a gauge face. |
| `RoundOuter = 16` |  |

## LvScrollbarMode

`enum` in `LVGL.Net`

Scrollbar visibility policy.

| Value | Description |
|--------|-------------|
| `Off = 0` |  |
| `On = 1` |  |
| `Active = 2` |  |
| `Auto = 3` |  |

## LvState

`enum` in `LVGL.Net`

Interaction state of a widget.

Mirrors `lv_state_t` of the bundled LVGL. These values are not stable across LVGL
releases - 9.5 inserted `LV_STATE_ALT` at bit 0 and moved every other state up - so the
integration tests check each one against the native build through `lvn_constant`.

| Value | Description |
|--------|-------------|
| `Default = 0` |  |
| `Alt = 1` | Alternative look, e.g. a dark variant of a theme. |
| `Checked = 4` |  |
| `Focused = 8` |  |
| `FocusKey = 16` |  |
| `Edited = 32` |  |
| `Hovered = 64` |  |
| `Pressed = 128` |  |
| `Scrolled = 256` |  |
| `Disabled = 512` |  |
| `User1 = 4096` |  |
| `User2 = 8192` |  |
| `User3 = 16384` |  |
| `User4 = 32768` |  |
| `Any = 65535` |  |

## LvSymbols

`static class` in `LVGL.Net`

Icon glyphs from LVGL's built-in symbol font, for use inside label and button text.

LVGL's `LV_SYMBOL_*` macros are Private Use Area code points that its fonts map to
FontAwesome glyphs. They are built here from their numeric code points rather than pasted in as
literals, so the source file stays plain ASCII and cannot be corrupted by an editor that
re-encodes it. The UTF-8 marshaller emits exactly the three-byte sequences LVGL expects.





Concatenate with normal text, e.g. `$"{LvSymbols.Warning} Sensor offline"`.

| Member | Description |
|--------|-------------|
| `string Audio { get; }` |  |
| `string Backspace { get; }` |  |
| `string Bars { get; }` |  |
| `string BatteryEmpty { get; }` |  |
| `string BatteryFull { get; }` |  |
| `string BatteryHalf { get; }` |  |
| `string BatteryQuarter { get; }` |  |
| `string BatteryThreeQuarters { get; }` |  |
| `string Bell { get; }` |  |
| `string Bluetooth { get; }` |  |
| `string Call { get; }` |  |
| `string Charge { get; }` |  |
| `string Close { get; }` |  |
| `string Copy { get; }` |  |
| `string Cut { get; }` |  |
| `string Directory { get; }` |  |
| `string Down { get; }` |  |
| `string Download { get; }` |  |
| `string Drive { get; }` |  |
| `string Edit { get; }` |  |
| `string Eject { get; }` |  |
| `string Envelope { get; }` |  |
| `string File { get; }` |  |
| `string Gps { get; }` |  |
| `string Home { get; }` |  |
| `string Image { get; }` |  |
| `string Keyboard { get; }` |  |
| `string Left { get; }` |  |
| `string List { get; }` |  |
| `string Loop { get; }` |  |
| `string Minus { get; }` |  |
| `string Mute { get; }` |  |
| `string Next { get; }` |  |
| `string Ok { get; }` |  |
| `string Pause { get; }` |  |
| `string Play { get; }` |  |
| `string Plus { get; }` |  |
| `string Power { get; }` |  |
| `string Previous { get; }` |  |
| `string Refresh { get; }` |  |
| `string Right { get; }` |  |
| `string Save { get; }` |  |
| `string SdCard { get; }` |  |
| `string Settings { get; }` |  |
| `string Stop { get; }` |  |
| `string Trash { get; }` |  |
| `string Up { get; }` |  |
| `string Upload { get; }` |  |
| `string Usb { get; }` |  |
| `string Video { get; }` |  |
| `string VolumeMax { get; }` |  |
| `string VolumeMid { get; }` |  |
| `string Warning { get; }` |  |
| `string Wifi { get; }` |  |

## LvTextAlign

`enum` in `LVGL.Net`

Text alignment inside a widget.

| Value | Description |
|--------|-------------|
| `Auto = 0` |  |
| `Left = 1` |  |
| `Center = 2` |  |
| `Right = 3` |  |

## LvTimer

`sealed class` in `LVGL.Net`

Runs a callback periodically on the LVGL thread, inside the run loop.

The UI-thread counterpart to a `Timer` plus
[`Post`](#lvglapplication): the tick runs between frames with LVGL already owned, so
it may touch widgets directly and needs no queue. Use it for clocks, blinking indicators and
polling cheap state. Work that blocks belongs on a background thread instead - a slow tick
stalls rendering.





Ticks are timed by LVGL's clock, so a timer only advances while the run loop does.




**Lifetime.** Dispose the timer to stop and free it. A running timer is kept alive by
LVGL.Net, so a fire-and-forget timer is not collected. LVGL's own auto-delete is switched off:
it would free the timer behind the wrapper's back when [`RepeatCount`](#lvtimer) ran out, so
instead an exhausted timer pauses and waits to be disposed or restarted.

| Member | Description |
|--------|-------------|
| `LvTimer(int periodMs, Action<LvTimer> tick, int repeatCount = -1)` | Creates and starts a timer.<br>`periodMs`: Milliseconds between ticks.<br>`tick`: Runs on the UI thread at each tick.<br>`repeatCount`: Number of ticks before pausing; [`Infinite`](#lvtimer) to keep going. |
| `bool IsAlive { get; }` | False once disposed. |
| `bool IsPaused { get; }` | True when paused, by [`Pause`](#lvtimer) or because the repeat count ran out. |
| `int LiveCount { get; }` | Number of live timers created through LVGL.Net. |
| `int Period { get; set; }` | Milliseconds between ticks. |
| `int RepeatCount { get; }` | The repeat count the timer was last started with. |
| `long TickCount { get; }` | Number of ticks so far. |
| `void Dispose()` | Stops and frees the timer. |
| `void FireSoon()` | Makes the next tick happen on the next run-loop iteration. |
| `void Pause()` | Suspends ticking. |
| `void ResetPeriod()` | Restarts the period from now. |
| `void Restart(int repeatCount = -1)` | Restarts an exhausted or paused timer with a new repeat count. |
| `void Resume()` | Resumes ticking. |
| `const int Infinite` | Pass as [`RepeatCount`](#lvtimer) to repeat forever (the default). |

## LvglAbout

`static class` in `LVGL.Net`

Provenance of this library, for about screens, log banners and `--version` output.

The values come from assembly metadata written by `Directory.Build.props`, so the credit
travels with the binary instead of living only in the README - and there is one place to
change it.

| Member | Description |
|--------|-------------|
| `string Banner { get; }` | Multi-line banner suitable for a console header. |
| `string Credit { get; }` | Single-line credit, in Bahasa Indonesia as written by the author. |
| `string CreditEnglish { get; }` | Single-line credit in English. |
| `string Organization { get; }` | The organisation behind the port. |
| `string PortedBy { get; }` | Who ported LVGL to .NET. |
| `string Version { get; }` | Version of the LVGL.Net assemblies. |

## LvglApplication

`sealed class` in `LVGL.Net`

Owns the LVGL run loop: the tick source, the timer handler, the display and the input devices.

```csharp
using var backend = new SdlBackend(800, 480, "Demo");
using var app = LvglApplication.Create(backend);

var button = new LvButton(app.Screen) { Text = "Tap me" };
button.Center();
button.Clicked += (_, _) => Console.WriteLine("clicked");

app.Run();
```

| Member | Description |
|--------|-------------|
| `ILvglBackend Backend { get; }` | The backend supplying the surface and input. |
| `LvglDisplay Display { get; }` | The display this application renders through. |
| `LvGroup FocusGroup { get; }` | The keyboard focus group, or null when [`EnableKeyboard`](#lvgloptions) is off. Focusable widgets join it automatically as they are created. |
| `long FrameCount { get; }` | Frames completed since the loop started. |
| `int NextIdleMs { get; }` | Idle time in milliseconds reported by the most recent timer pass. |
| `LvScreen Screen { get; }` | The active screen, i.e. the root object new widgets are usually parented to. |
| `static LvglApplication Create(ILvglBackend backend, LvglOptions options = null)` | Initialises LVGL if needed and wires it to `backend`. The calling thread becomes the UI thread; every widget call must happen on it (see [`Post`](#lvglapplication)). |
| `void Dispose()` |  |
| `void Invoke(Action action)` | Runs `action` on the UI thread and waits for it to finish. |
| `void Post(Action action)` | Queues `action` to run on the UI thread at the start of the next iteration. This is the supported way for background threads - a sensor poller, a network client - to touch widgets. |
| `bool RunFrame()` | Advances LVGL by one iteration: drains posted work, feeds the tick, runs timers and redraws, then pumps host events.<br>Returns: `false` when the backend asked to close. |
| `void Run(CancellationToken cancellationToken = default)` | Runs the loop until the backend reports a close request, [`Stop`](#lvglapplication) is called or `cancellationToken` fires. |
| `void Stop()` | Asks [`Run`](#lvglapplication) to return after the current iteration. |
| `event EventHandler FrameCompleted` | Raised on the UI thread after each run-loop iteration. |

## LvglCallbackFailedEventArgs

`sealed class` in `LVGL.Net`

Describes an exception caught where LVGL called into managed code.

| Member | Description |
|--------|-------------|
| `LvglCallbackFailedEventArgs(string source, Exception exception)` | Describes an exception caught where LVGL called into managed code. |
| `Exception Exception { get; }` | The exception the managed code threw. |
| `string Source { get; }` | What was being called back: "event Clicked on LvButton", "timer", "animation". |

## LvglDemos

`static class` in `LVGL.Net`

The demo scenes bundled with LVGL, available when the native library was built with
`LVGLNET_WITH_DEMOS=ON` (the default).

These build their widget tree entirely in C, so the objects they create have no managed
wrappers. They are useful as a rendering smoke test and as a performance baseline for a new
board - not as a starting point for application code.

| Member | Description |
|--------|-------------|
| `static bool ShowBenchmark()` | Runs LVGL's rendering benchmark, which reports its results on screen.<br>Returns: `false` when the native library was built without the demos. |
| `static bool ShowWidgets()` | Builds LVGL's widget showcase on the active screen.<br>Returns: `false` when the native library was built without the demos. |

## LvglException

`class` in `LVGL.Net`

Error raised by the LVGL.Net wrapper.

| Member | Description |
|--------|-------------|
| `LvglException(string message)` |  |
| `LvglException(string message, Exception innerException)` |  |

## LvglOptions

`sealed class` in `LVGL.Net`

Tuning knobs for [`LvglApplication`](#lvglapplication).

| Member | Description |
|--------|-------------|
| `LvglOptions()` |  |
| `bool EnableKeyboard { get; set; }` | Create a keypad input device. Requires the backend to implement [`ILvglKeyboardSource`](Lvgl.Backends.md#ilvglkeyboardsource). |
| `bool EnablePointer { get; set; }` | Create a pointer input device from the backend. Enabled by default. |
| `int MaxIdleSleepMs { get; set; }` | Upper bound on how long the run loop sleeps between iterations, in milliseconds. LVGL may report that nothing is due for a long time; capping the sleep keeps input responsive. |
| `int MinIdleSleepMs { get; set; }` | Lower bound on the sleep between iterations. Zero busy-waits, which burns a core for nothing on a Pi, so the default yields for a millisecond instead. |
| `int PartialBufferLines { get; set; }` | Scan lines per draw buffer in partial mode. Two buffers of this size are allocated. The default of 0 means "one tenth of the screen height", LVGL's own recommendation. |
| `LvRenderMode RenderMode { get; set; }` | How LVGL hands rendered pixels to the backend. [`Partial`](#lvrendermode) keeps memory use to [`PartialBufferLines`](#lvgloptions) scan lines and is the right default for embedded targets; [`Direct`](#lvrendermode) trades a full-screen buffer for fewer blit calls per frame. |

## LvglRuntime

`static class` in `LVGL.Net`

Process-wide LVGL state: initialisation, version checks and UI-thread affinity.

LVGL keeps its object tree, style cache and timer list in globals guarded by no lock at all, so
exactly one thread may touch it. LVGL.Net enforces that by recording the thread that called
[`Initialize`](#lvglruntime) and checking it on the operations most likely to be called from a
worker (see [`EnsureUiThread`](#lvglruntime)). Background work should go through
[`Post`](#lvglapplication) instead.

| Member | Description |
|--------|-------------|
| `uint ColorDepth { get; }` | Colour depth the native library was compiled with. LVGL.Net requires 32. |
| `bool IsInitialized { get; }` | True once [`Initialize`](#lvglruntime) has completed. |
| `bool IsUiThread { get; }` | True when the current thread owns LVGL. |
| `Version LvglVersion { get; }` | Version of the LVGL build that was actually loaded. |
| `Version MinimumLvglVersion { get; }` | Minimum LVGL version this wrapper is written against. |
| `static void EnsureUiThread()` | Throws when called from a thread other than the one that initialised LVGL. |
| `static void Initialize()` | Loads the native library, validates it and calls `lv_init`. Safe to call repeatedly; only the first call does work. The calling thread becomes the UI thread. |
| `static void Shutdown()` | Tears LVGL down. Intended for tests and for hosts that create and destroy the UI repeatedly; long-running applications normally just exit the process. |
| `const uint ExpectedShimAbi` | ABI version the managed side expects from the native shim. Kept in step with `LVN_ABI_VERSION` in `native/shim/lvglnet_shim.h`. |
| `event EventHandler<LvglCallbackFailedEventArgs> CallbackFailed` | Raised when managed code called back from LVGL - an event handler, a timer, an animation - throws. |

## LvglThreadAccessException

`sealed class` in `LVGL.Net`

Thrown when LVGL is touched from a thread that does not own it.

| Member | Description |
|--------|-------------|
| `LvglThreadAccessException(string message)` | Thrown when LVGL is touched from a thread that does not own it. |

