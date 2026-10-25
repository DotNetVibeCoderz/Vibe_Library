# Lvgl.Styling

[API reference](README.md) · generated from the XML documentation; do not edit by hand.

## LvStyle

`sealed class` in `LVGL.Net`

A reusable LVGL style that can be applied to many widgets.

Prefer this over per-widget style setters when the same look is applied repeatedly: LVGL stores
one property table and the widgets reference it, instead of every widget carrying its own copy.




**Lifetime matters.** LVGL stores a bare pointer to the style, so the style must outlive every
widget that uses it. Disposing a style that is still applied leaves dangling references and will
crash on the next redraw - keep styles in a field or a static, not in a `using` block that
closes before the UI does.

| Member | Description |
|--------|-------------|
| `LvStyle()` | Allocates a new, empty style. |
| `nint Handle { get; }` | Native `lv_style_t*`. |
| `LvStyle ArcColor(LvColor color)` |  |
| `LvStyle BackgroundColor(LvColor color)` |  |
| `LvStyle BackgroundGradientColor(LvColor color)` |  |
| `LvStyle BackgroundOpacity(byte opacity)` | Background opacity, 0 (transparent) to 255 (opaque). |
| `LvStyle BorderColor(LvColor color)` |  |
| `LvStyle BorderWidth(int width)` |  |
| `void Dispose()` | Frees the style. Only call this once every widget referencing it has been deleted. |
| `LvStyle Font(LvFont font)` | Sets a font; a TrueType [`LvFont`](Lvgl.Drawing.md#lvfont) must outlive the style. |
| `LvStyle FontSize(int size)` | Selects a built-in Montserrat font size. |
| `LvStyle LineColor(LvColor color)` |  |
| `LvStyle Padding(int padding)` |  |
| `LvStyle Radius(int radius)` |  |
| `LvStyle Size(int width, int height)` |  |
| `LvStyle TextColor(LvColor color)` |  |

