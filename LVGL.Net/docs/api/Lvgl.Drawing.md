# Lvgl.Drawing

[API reference](README.md) · generated from the XML documentation; do not edit by hand.

## LvArea

`readonly struct` in `LVGL.Net`

An inclusive rectangle, matching LVGL's `lv_area_t` semantics: [`X2`](#lvarea) and
[`Y2`](#lvarea) are the last pixel *inside* the area, so a 1x1 area has `X1 == X2`.

| Member | Description |
|--------|-------------|
| `LvArea(int x1, int y1, int x2, int y2)` | An inclusive rectangle, matching LVGL's `lv_area_t` semantics: [`X2`](#lvarea) and [`Y2`](#lvarea) are the last pixel *inside* the area, so a 1x1 area has `X1 == X2`. |
| `int Height { get; }` | Height in pixels (inclusive bounds). |
| `bool IsEmpty { get; }` | True when the area covers no pixels. |
| `int Width { get; }` | Width in pixels (inclusive bounds). |
| `int X1 { get; }` |  |
| `int X2 { get; }` |  |
| `int Y1 { get; }` |  |
| `int Y2 { get; }` |  |
| `LvArea Clip(in LvArea bounds)` | Clips this area to `bounds`. |
| `static LvArea FromSize(int x, int y, int width, int height)` | Creates an area from a position and size. |
| `string ToString()` |  |

## LvColor

`readonly struct` in `LVGL.Net`

A 24-bit RGB colour.

Deliberately *not* a mirror of the native `lv_color_t`: that type is a three-byte struct
whose by-value calling convention differs between Windows x64 and System V, so LVGL.Net never
passes it across the boundary. Colours travel as a packed `0xRRGGBB``UInt32`
and the native shim converts them with `lv_color_hex`.

| Member | Description |
|--------|-------------|
| `LvColor(byte r, byte g, byte b)` | Creates a colour from 8-bit components. |
| `LvColor(uint rgb)` | Creates a colour from packed `0xRRGGBB`. The upper byte is ignored. |
| `byte B { get; }` |  |
| `LvColor Black { get; }` |  |
| `LvColor Blue { get; }` |  |
| `LvColor Cyan { get; }` |  |
| `LvColor DarkGrey { get; }` |  |
| `byte G { get; }` |  |
| `LvColor Green { get; }` |  |
| `LvColor Grey { get; }` |  |
| `LvColor Orange { get; }` |  |
| `LvColor Purple { get; }` |  |
| `byte R { get; }` |  |
| `LvColor Red { get; }` |  |
| `uint Rgb { get; }` | Packed `0xRRGGBB` value handed to the native side. |
| `LvColor White { get; }` |  |
| `LvColor Yellow { get; }` |  |
| `LvColor ContrastingText()` | Black or white, whichever reads better on this colour. |
| `LvColor Darken(float amount)` | Returns a darker shade; `amount` 0 = unchanged, 1 = black. |
| `bool Equals(LvColor other)` |  |
| `bool Equals(object obj)` |  |
| `static LvColor FromRgb(byte r, byte g, byte b)` | Creates a colour from 8-bit components. |
| `static LvColor FromRgb(uint rgb)` | Creates a colour from packed `0xRRGGBB`. |
| `int GetHashCode()` |  |
| `LvColor Lerp(LvColor other, float amount)` | Blends towards `other`. `amount` is clamped to 0..1. |
| `LvColor Lighten(float amount)` | Returns a lighter tint; `amount` 0 = unchanged, 1 = white. |
| `float Luminance()` | Relative luminance per WCAG 2.x, used to pick readable foreground text over this colour. |
| `static LvColor Parse(string text)` | Parses a hex colour, throwing on malformed input. |
| `string ToString()` |  |
| `uint ToXrgb8888()` | The XRGB8888 word this colour occupies in a 32-bit LVGL frame buffer. |
| `static bool TryParse(string text, out LvColor color)` | Parses `#RGB`, `#RRGGBB` or bare hex. Returns `false` instead of throwing so it can back designer text boxes directly. |

## LvCoord

`static class` in `LVGL.Net`

Helpers for LVGL's "special" coordinate values.

LVGL encodes percentages and content-sizing in the high bits of a normal `int32_t`
coordinate rather than using a separate type, via the `LV_PCT` / `LV_SIZE_CONTENT`
macros. Macros do not survive into a shared library, so the encoding is reproduced here in
managed code - it is on the layout path and a P/Invoke per coordinate would not pay for itself.





The constants mirror `lv_area.h` of LVGL v9 exactly. Note that v9 changed both macros from
v8: `LV_SIZE_CONTENT` became `LV_COORD_MAX` rather than a fixed 2001, and negative
percentages are stored relative to `LV_PCT_POS_MAX` rather than 1000. An integration test
cross-checks every value produced here against LVGL's own `lv_pct`, so a future change
upstream fails the build rather than silently misplacing widgets.

| Member | Description |
|--------|-------------|
| `static int GetPercent(int coord)` | Recovers the percentage from a value produced by [`Percent`](#lvcoord). |
| `static bool IsPercent(int coord)` | True when `coord` is specifically a percentage. |
| `static bool IsSpecial(int coord)` | True when `coord` is a percentage or [`SizeContent`](#lvcoord). |
| `static int Percent(int value)` | A percentage of the parent's corresponding dimension (`LV_PCT`). |
| `static int Plain(int coord)` | Strips the encoding bits, yielding the raw number a special coordinate carries. |
| `const int CoordMax` | Largest plain pixel coordinate LVGL can represent (`LV_COORD_MAX`). |
| `const int PercentMax` | Largest percentage LVGL can store (`LV_PCT_POS_MAX`). |
| `const int SizeContent` | Size the widget to fit its content (`LV_SIZE_CONTENT`). |

## LvFileSystem

`static class` in `LVGL.Net`

Maps host paths onto LVGL's drive-letter file system.

The native library registers the C library's `fopen` as LVGL drive `A:`. Every path
handed to LVGL must carry that prefix: without it LVGL reads the first character as the drive,
so a Windows path such as `C:\img.png` would be looked up on a drive "C" that does not exist.

| Member | Description |
|--------|-------------|
| `static string ToLvglPath(string path)` | Converts a host path into the `"A:/full/path"` form LVGL expects. |
| `const Char DriveLetter` | The drive letter configured in `native/lv_conf.h` (`LV_FS_STDIO_LETTER`). |

## LvFont

`sealed class` in `LVGL.Net`

A font: one of the Montserrat sizes compiled into the native library, or a TrueType font
rasterised at run time by LVGL's TinyTTF.

TrueType is what makes scripts outside Montserrat's Latin range usable - Arabic, Thai, CJK -
and lets an application ship its own brand font without rebuilding the native library.
Glyphs are rendered on demand and cached, so the first frame that uses a new glyph costs more
than later ones.




**Lifetime matters**, exactly as for [`LvStyle`](Lvgl.Styling.md#lvstyle): widgets keep a bare
pointer to the font, so dispose it only after every widget using it has been deleted. Built-in
fonts are owned by the native library and their [`Dispose`](#lvfont) does nothing.

| Member | Description |
|--------|-------------|
| `nint Handle { get; }` | Native `lv_font_t*`. |
| `bool IsTrueType { get; }` | True for a TinyTTF font, false for a built-in bitmap font. |
| `int Size { get; set; }` | Pixel size. Settable for TrueType fonts, which re-rasterise; built-in fonts have one size. |
| `void Dispose()` | Frees a TrueType font. Only call this once every widget using it is deleted. |
| `static LvFont FromData(ReadOnlySpan<byte> ttf, int size)` | Loads a TrueType font from memory - an embedded resource, for example. The bytes are copied into unmanaged memory that lives as long as the font, because TinyTTF reads glyphs from them on demand. |
| `static LvFont FromFile(string path, int size)` | Loads a TrueType font from a file. LVGL reads the file lazily as glyphs are needed. |
| `static LvFont Montserrat(int size)` | The closest built-in Montserrat size (12, 14, 16, 20, 24, 28, 36). Sizes that are not compiled in fall back to the default font rather than failing. |

## LvImageSource

`sealed class` in `LVGL.Net`

Pixels or a file for an [`LvImage`](Lvgl.Widgets.md#lvimage) to show.

Two kinds exist. A **file** source is just a path: LVGL opens and decodes it (PNG and BMP are
compiled in) and caches the decoded pixels. A **pixel** source wraps a copy of caller-supplied
ARGB pixels in unmanaged memory, for images produced at run time - a camera frame, a rendered
QR code, a thumbnail decoded by the application.




**Lifetime.** A pixel source must outlive every image showing it, because LVGL draws straight
from its memory - the same rule as [`LvStyle`](Lvgl.Styling.md#lvstyle). Disposing also evicts it from
LVGL's image cache, so the address cannot be mistaken for a later image.

| Member | Description |
|--------|-------------|
| `string FilePath { get; }` | The file this source refers to, or null for a pixel source. |
| `int Height { get; }` | Pixel height; 0 for a file source. |
| `bool IsFile { get; }` | True for a file source. |
| `int Width { get; }` | Pixel width; 0 for a file source, whose size is known only once LVGL decodes it. |
| `void Dispose()` | Releases the pixels. Only call this once no image shows the source any more; must run on the LVGL thread because it evicts the image from LVGL's cache. |
| `static LvImageSource FromArgb(int width, int height, ReadOnlySpan<uint> argb)` | Copies 32-bit pixels into an image. Each value is `0xAARRGGBB`; alpha is honoured.<br>`width`: Width in pixels.<br>`height`: Height in pixels.<br>`argb`: Row-major pixels, `width` × `height` of them. |
| `static LvImageSource FromFile(string path)` | Refers to an image file. PNG and BMP are decoded by the native library. |
| `static LvImageSource Solid(int width, int height, LvColor color, byte alpha = 255)` | A solid-colour image - handy for placeholders and swatches. |

