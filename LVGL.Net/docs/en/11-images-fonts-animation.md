# Images, fonts and animation

## Images

`LvImage` shows a file, pixels made at run time, or a built-in symbol.

```csharp
// A file: PNG and BMP are decoded by the native library and cached.
var logo = new LvImage(app.Screen);
logo.SetFile("assets/logo.png");

// Pixels produced by the application: 0xAARRGGBB, alpha honoured.
var pixels = new uint[64 * 64];
// ... fill ...
var source = LvImageSource.FromArgb(64, 64, pixels);
var preview = new LvImage(app.Screen, source);

// A symbol glyph, drawn in the text colour.
var icon = new LvImage(app.Screen);
icon.SetSymbol(LvSymbols.Wifi);
icon.SetTextColor(LvColor.White);
```

| Property | Meaning |
|---|---|
| `Rotation` | Tenths of a degree; 900 is a quarter turn, about the pivot (`SetPivot`) |
| `Scale` | 256 is 100 %, 512 is 200 % |
| `InnerAlign` | How the pixels sit in a widget of a different size: `Center`, `Stretch`, `Tile`, `Contain`, `Cover`... |
| `Antialias` | Smooth rotated and scaled images, at some cost |
| `SourceWidth`, `SourceHeight` | The source's size once LVGL has read it |

**Lifetime.** A pixel source wraps unmanaged memory LVGL draws straight from, so it follows the
`LvStyle` rule: dispose it after every image showing it has been deleted. Disposing also evicts it
from LVGL's image cache, so its address can never be mistaken for a later image. A file source is
just a path and needs no disposal.

**Paths.** LVGL addresses files through drive letters; the native build maps the C library's
`fopen` to drive `A:`. Every LVGL.Net API adds that prefix for you (`LvFileSystem.ToLvglPath`) - pass
ordinary paths. A missing file throws `FileNotFoundException` before LVGL sees it.

## Fonts

The native library carries Montserrat at 12, 14, 16, 20, 24, 28 and 36 px (`SetFontSize`). For any
other size, a brand font, or a script Montserrat does not cover - Arabic, Thai, CJK - load a
TrueType font, rasterised at run time by LVGL's TinyTTF:

```csharp
_brand = LvFont.FromFile("assets/Inter.ttf", 18);
title.SetFont(_brand);

// or from an embedded resource
_symbols = LvFont.FromData(File.ReadAllBytes("NotoSansThai.ttf"), 20);

_brand.Size = 22;     // re-rasterise at another size
```

Glyphs are rendered on first use and cached, so the first frame that shows a new character costs a
little more than later ones. Like a style, a TrueType font must outlive the widgets using it:
delete the widgets, then dispose the font. `LvFont.Montserrat(size)` returns a built-in font as an
`LvFont`, which is never freed.

`LvStyle.Font(font)` applies a font through a shared style. In a layout document, `Font` on a node
or a shared style names a TrueType asset, sized by `FontSize`.

## Animation

`LvAnimation` animates an integer from one value to another on LVGL's own animation timer, and
calls you with each frame's value - so anything an integer can drive can be animated.

```csharp
new LvAnimation(card, x => card.X = x, from: -200, to: 16)
{
    Duration = 400,
    Path = LvAnimPath.EaseOut,
}.Start();
```

| Property | Meaning |
|---|---|
| `Duration`, `Delay` | Milliseconds |
| `Path` | `Linear`, `EaseIn`, `EaseOut`, `EaseInOut`, `Overshoot`, `Bounce`, `Step` |
| `ReverseDuration`, `ReverseDelay` | Play back to the start after each forward run |
| `RepeatCount`, `RepeatDelay` | `LvAnimation.Infinite` repeats forever |
| `ApplyFromImmediately` | Apply the start value at once even with a delay, so the widget does not jump |
| `Completed` | Raised when it finishes by itself (not on `Stop`) |

Shortcuts cover the common cases:

```csharp
panel.AnimateX(200);
panel.AnimateOpacity(from: 0, to: 255, durationMs: 250);
bar.AnimateValue(80);
```

**Lifetime.** A running animation is kept alive by LVGL.Net - no field needed. Pass the widget it
moves as the target (the shortcuts do), and deleting the widget stops the animation; without that,
the next frame would write to a deleted widget. Animations are identified to LVGL by an id rather
than by the widget, so `Stop()` stops exactly one animation even when several run on the same
widget.

## Timers

`LvTimer` runs a callback periodically **on the LVGL thread, inside the run loop**, so it may
touch widgets directly - no `Post` needed:

```csharp
_clock = new LvTimer(1000, _ => time.Text = DateTime.Now.ToString("HH:mm:ss"));
_blink = new LvTimer(500, _ => led.Toggle(), repeatCount: 6);   // pauses after 6 ticks
```

`Pause`, `Resume`, `Period`, `FireSoon` (tick on the next loop pass), `Restart(repeatCount)` and
`TickCount` do what they say. Dispose a timer to stop and free it. A timer whose repeat count runs
out pauses rather than freeing itself - LVGL's auto-delete is off, so the wrapper never holds a
freed pointer.

Work that blocks - file or network I/O, a slow sensor - still belongs on a background thread with
`Post`: a timer tick runs between frames, and a slow one stalls rendering.

## When a callback throws

An exception from an animation or timer callback (or an event handler) is caught before it can
unwind into LVGL's C code, and reported:

```csharp
LvglRuntime.CallbackFailed += (_, e) => Console.Error.WriteLine($"{e.Source}: {e.Exception}");
```

`e.Source` says what was running - `"timer"`, `"animation"`, `"event Clicked on LvButton"`.
