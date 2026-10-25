# Lvgl.Display

[API reference](README.md) · generated from the XML documentation; do not edit by hand.

## LvglDisplay

`sealed class` in `LVGL.Net`

Binds an [`ILvglBackend`](Lvgl.Backends.md#ilvglbackend) to a native `lv_display_t` and owns its draw buffers.

| Member | Description |
|--------|-------------|
| `long AllocatedBytes { get; }` | Total bytes of unmanaged memory held for draw buffers. |
| `int BufferLines { get; }` | Height in scan lines of each draw buffer. |
| `uint BufferSizeBytes { get; }` | Size of each of the two draw buffers, in bytes. |
| `long FlushCount { get; }` | Number of regions flushed since creation. Useful when profiling redraw churn. |
| `nint Handle { get; }` | Native `lv_display_t*`. |
| `int Height { get; }` |  |
| `LvRenderMode RenderMode { get; }` | Render mode this display was created with. |
| `int Width { get; }` |  |
| `void Dispose()` |  |
| `void RefreshNow()` | Renders everything that is invalid right now, instead of at the next refresh tick - for a screenshot taken straight after a change, or for measuring a redraw on its own. |
| `const int BytesPerPixel` | Bytes per pixel of the render buffer. Fixed at 4 by LV_COLOR_DEPTH=32. |

