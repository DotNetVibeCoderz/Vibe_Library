# Lvgl.Sdl

[API reference](README.md) · generated from the XML documentation; do not edit by hand.

## SdlBackend

`sealed class` in `LVGL.Net.Sdl`

Renders LVGL into an SDL2 window and feeds it mouse and keyboard input. Works anywhere SDL2
does: Windows, macOS, and Linux desktops including a Raspberry Pi running X11 or Wayland.

LVGL renders into a buffer this class owns; each flushed region is pushed into a streaming
texture and the texture is presented once per frame. The texture retains its contents between
frames, which is what makes LVGL's partial redraw mode correct here - only the changed strips
are uploaded.

| Member | Description |
|--------|-------------|
| `SdlBackend(int width, int height, string title = "LVGL.Net", bool vsync = true)` | Opens a window and prepares the rendering pipeline.<br>`width`: Surface width in pixels.<br>`height`: Surface height in pixels.<br>`title`: Window caption.<br>`vsync`: Synchronise presentation to the display refresh. Leave on for desktop use; turning it off is mainly useful when benchmarking, where a capped frame rate hides the real cost. |
| `int Height { get; }` |  |
| `LvPointerState Pointer { get; }` |  |
| `int Width { get; }` |  |
| `void Blit(in LvArea area, nint pixels, bool isLastFlush)` |  |
| `void Dispose()` |  |
| `bool PumpEvents()` |  |
| `void SetTitle(string title)` | Changes the window caption. |
| `bool TryDequeueKey(out uint key, out bool pressed)` |  |

