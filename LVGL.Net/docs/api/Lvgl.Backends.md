# Lvgl.Backends

[API reference](README.md) · generated from the XML documentation; do not edit by hand.

## HeadlessBackend

`sealed class` in `LVGL.Net`

Renders into a managed pixel buffer instead of a screen.

Three things use this: the designer, which copies [`Frame`](#headlessbackend) into a bitmap to show a
true LVGL preview; the test suite, which asserts on rendered pixels without a display server;
and anything that wants a screenshot ([`SavePng`](#headlessbackend)). Input can be driven synthetically through
[`SetPointer`](#headlessbackend).

| Member | Description |
|--------|-------------|
| `HeadlessBackend(int width, int height)` | Creates an off-screen surface. |
| `byte[] Buffer { get; }` | The backing array, for APIs that need one (e.g. `WriteableBitmap.WritePixels`). |
| `LvArea DirtyArea { get; }` | Union of the regions blitted since [`ClearDirty`](#headlessbackend). |
| `ReadOnlySpan<byte> Frame { get; }` | The rendered surface in XRGB8888, laid out top-down with no row padding beyond [`Stride`](#headlessbackend). Valid to read between run-loop iterations. |
| `int Height { get; }` |  |
| `bool IsDirty { get; }` | True when at least one region was blitted since [`ClearDirty`](#headlessbackend). |
| `int PendingKeys { get; }` | Number of key events still waiting to be read by LVGL. |
| `LvPointerState Pointer { get; }` |  |
| `int Stride { get; }` | Bytes per row of [`Frame`](#headlessbackend). |
| `int Width { get; }` |  |
| `void Blit(in LvArea area, nint pixels, bool isLastFlush)` |  |
| `void ClearDirty()` | Resets [`IsDirty`](#headlessbackend) and [`DirtyArea`](#headlessbackend) after consuming a frame. |
| `void CopyTo(nint destination, int destinationStride)` | Copies the frame to unmanaged memory, e.g. a locked bitmap's back buffer. |
| `void CopyTo(Span<byte> destination)` | Copies the frame into a caller-supplied buffer, converting nothing. The destination must be at least [`Stride`](#headlessbackend) * [`Height`](#headlessbackend) bytes. |
| `void Dispose()` |  |
| `void PressKey(uint key)` | Queues a complete keystroke: a press followed by a release. |
| `bool PumpEvents()` | Always true - there is no window to close. |
| `void SavePng(string path)` | Writes the current frame as a PNG - a screenshot of exactly what LVGL rendered. Run a frame (or [`RefreshNow`](Lvgl.Display.md#lvgldisplay)) first so the surface is up to date. |
| `void SendKey(uint key, bool pressed = true)` | Queues a key event, as though a keyboard had reported it. |
| `void SendText(string text)` | Queues a full keystroke per character of `text`. |
| `void SetPointer(int x, int y, bool pressed)` | Injects pointer state, as though a mouse or touch panel had reported it. |
| `bool TryDequeueKey(out uint key, out bool pressed)` |  |
| `const int BytesPerPixel` | Bytes per pixel in [`Frame`](#headlessbackend) (XRGB8888). |
| `event EventHandler FrameRendered` | Raised on the UI thread after the final region of a frame has been blitted. |

## ILvglBackend

`interface` in `LVGL.Net`

Everything platform-specific that LVGL.Net needs: somewhere to put pixels and somewhere to get
input from.

The whole point of this abstraction is that the native side stays a plain, driver-free LVGL
build. LVGL renders into a buffer LVGL.Net owns and calls [`Blit`](#ilvglbackend); how those pixels
reach a screen is entirely managed code. That is what makes the same wrapper work on an SDL
window, a Raspberry Pi framebuffer and an off-screen WPF preview surface without recompiling C.





All members are called from the LVGL UI thread only.

| Member | Description |
|--------|-------------|
| `int Height { get; }` | Height of the surface in pixels. |
| `LvPointerState Pointer { get; }` | Pointer state sampled by the most recent [`PumpEvents`](#ilvglbackend) call. |
| `int Width { get; }` | Width of the surface in pixels. |
| `void Blit(in LvArea area, nint pixels, bool isLastFlush)` | Copies one rendered region to the surface.<br>`area`: Destination rectangle, inclusive on both corners.<br>`pixels`: Pointer to the rendered region in XRGB8888 (byte order B, G, R, X on little-endian machines), tightly packed with a stride of `area.Width * 4` bytes. Only valid for the duration of the call - copy, do not retain.<br>`isLastFlush`: True on the final region of a frame. Backends that present through a swap chain or a double-buffered device should flip here rather than on every region. |
| `bool PumpEvents()` | Processes host events and refreshes [`Pointer`](#ilvglbackend).<br>Returns: `false` when the user asked to close; the run loop then exits. |

## ILvglKeyboardSource

`interface` in `LVGL.Net`

Implemented by backends that can also deliver key presses to LVGL.

| Member | Description |
|--------|-------------|
| `bool TryDequeueKey(out uint key, out bool pressed)` | Dequeues one pending key event. LVGL polls one event per read cycle, so returning a single event per call is sufficient and keeps the queue in order. |

## LvPointerState

`readonly struct` in `LVGL.Net`

Current state of the pointing device (mouse, touch panel, ...).

| Member | Description |
|--------|-------------|
| `LvPointerState(int X, int Y, bool Pressed)` | Current state of the pointing device (mouse, touch panel, ...). |
| `bool Pressed { get; set; }` |  |
| `int X { get; set; }` |  |
| `int Y { get; set; }` |  |
| `void Deconstruct(out int X, out int Y, out bool Pressed)` |  |
| `bool Equals(LvPointerState other)` |  |
| `bool Equals(object obj)` |  |
| `int GetHashCode()` |  |
| `string ToString()` |  |

