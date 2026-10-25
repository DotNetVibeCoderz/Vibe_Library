# Lvgl.Linux

[API reference](README.md) · generated from the XML documentation; do not edit by hand.

## DrmBackend

`sealed class` in `LVGL.Net.Linux`

Draws LVGL through the kernel's DRM/KMS interface (`/dev/dri/card*`) with double
buffering and page flips on vertical blank - no tearing, and no window system.

Current Raspberry Pi OS drives displays through the KMS driver (`vc4-kms-v3d`); the fbdev
device that [`FramebufferBackend`](#framebufferbackend) uses is an emulation layer on top of it, which can
only copy into the visible image and therefore tears on movement. This backend talks to KMS
directly: it allocates two scan-out buffers, renders into the hidden one and flips them at
vertical blank.




**How a frame is presented.** LVGL renders partial strips into a shadow copy of the screen.
At the end of the frame the damaged region is copied into the hidden buffer and a page flip is
queued. The hidden buffer last showed the frame *before* the previous one, so the copy
covers this frame's damage and the previous frame's together - small regions stay small, and
neither buffer is ever stale. The wait for the flip happens at the start of the next present,
so LVGL renders the next frame while the display catches up.




**Permissions and ownership.** The device is owned by the `video` group, and only one
process may drive it: run this with no desktop on the same display (a console boot, or the
desktop stopped). If page flipping is refused by the driver, the backend falls back to drawing
straight into the visible buffer and reports it through [`IsDoubleBuffered`](#drmbackend).

| Member | Description |
|--------|-------------|
| `DrmBackend(string devicePath = null, IPointerSource pointerSource = null, int width = 0, int height = 0)` | Opens a DRM device and takes over its first connected display.<br>`devicePath`: The card, usually `/dev/dri/card0` or, on a Pi 4/5 running KMS, `/dev/dri/card1`. Null picks the first card that has a connected display.<br>`pointerSource`: Optional touch or mouse source, normally an [`EvdevPointer`](#evdevpointer).<br>`width`: Preferred mode width, or 0 for the display's preferred mode.<br>`height`: Preferred mode height, or 0 for the display's preferred mode. |
| `string DevicePath { get; }` | The device that was opened. |
| `long FramesPresented { get; }` | Frames presented so far. |
| `int Height { get; }` |  |
| `bool IsDoubleBuffered { get; }` | True when frames are presented by page flipping. False when the driver refused flips and the backend draws into the visible buffer instead (which can tear, like fbdev). |
| `string Mode { get; }` | The display mode in use, e.g. `800x480@60`. |
| `LvPointerState Pointer { get; }` |  |
| `int Width { get; }` |  |
| `void Blit(in LvArea area, nint pixels, bool isLastFlush)` |  |
| `void Dispose()` |  |
| `bool PumpEvents()` |  |

## EvdevPointer

`sealed class` in `LVGL.Net.Linux`

Reads a touchscreen or mouse through the Linux evdev interface (`/dev/input/eventN`).

Handles both kinds of device: touch panels report absolute positions (`EV_ABS`) in their
own coordinate space, which is rescaled to the screen using the range the driver advertises;
mice report relative motion (`EV_REL`), which is accumulated and clamped.




**Permissions.** Input devices belong to the `input` group:
`sudo usermod -aG input $USER`.

| Member | Description |
|--------|-------------|
| `EvdevPointer(string devicePath, int screenWidth, int screenHeight)` | Opens an input device and prepares to translate its events for a given screen size.<br>`devicePath`: Device node, for example `/dev/input/event0`.<br>`screenWidth`: Screen width, used to rescale absolute coordinates.<br>`screenHeight`: Screen height, used to rescale absolute coordinates. |
| `LvPointerState Pointer { get; }` |  |
| `static EvdevPointer AutoDetect(int screenWidth, int screenHeight)` | Picks the first device under `/dev/input` that looks like a touchscreen or mouse.<br>Returns: An open device, or `null` when none could be opened. |
| `void Dispose()` |  |
| `void Poll()` |  |

## FramebufferBackend

`sealed class` in `LVGL.Net.Linux`

Draws LVGL straight into a Linux framebuffer device (`/dev/fb0`), with no window system.

This is the backend for a Raspberry Pi driving a panel directly - a kiosk or instrument display
with no desktop running. It memory-maps the framebuffer and copies each flushed region into it,
so a redraw costs one `memcpy` per scan line and nothing else.




**Permissions.** The framebuffer is owned by the `video` group. Either add the user
(`sudo usermod -aG video $USER`, then log out and back in) or run as root.




**Console cursor.** The kernel keeps blinking the text cursor over the image; disable it with
`sudo sh -c 'echo 0 > /sys/class/graphics/fbcon/cursor_blink'`.

| Member | Description |
|--------|-------------|
| `FramebufferBackend(string devicePath = "/dev/fb0", IPointerSource pointerSource = null)` | Opens the framebuffer device.<br>`devicePath`: Framebuffer device, usually `/dev/fb0`.<br>`pointerSource`: Optional touch or mouse source; [`EvdevPointer`](#evdevpointer) is the usual choice. Without one the UI renders but cannot be interacted with. |
| `int BitsPerPixel { get; }` | Colour depth reported by the device: 32 and 16 bits per pixel are supported. |
| `int Height { get; }` |  |
| `LvPointerState Pointer { get; }` |  |
| `int Stride { get; }` | Bytes per row of the mapped framebuffer, which may exceed `Width * BytesPerPixel`. |
| `bool SwapRedBlue { get; }` | True when the device orders its 32-bit pixels as RGBA rather than the BGRA that LVGL produces, in which case the red and blue channels are swapped during the blit. |
| `int Width { get; }` |  |
| `void Blit(in LvArea area, nint pixels, bool isLastFlush)` |  |
| `void Dispose()` |  |
| `bool PumpEvents()` |  |

## IPointerSource

`interface` in `LVGL.Net.Linux`

A source of pointer state that must be polled once per frame.

| Member | Description |
|--------|-------------|
| `LvPointerState Pointer { get; }` | Latest known pointer state. |
| `void Poll()` | Drains pending device events. |

