# Backends

A backend is everything platform-specific: somewhere to put pixels, somewhere to get input from.
The native library has no display drivers at all, so this is the only place platform code lives.

```csharp
public interface ILvglBackend : IDisposable
{
    int Width { get; }
    int Height { get; }
    LvPointerState Pointer { get; }

    void Blit(in LvArea area, nint pixels, bool isLastFlush);
    bool PumpEvents();          // false => the user asked to close
}
```

All members are called from the LVGL thread only.

## SDL — `LVGL.Net.Sdl`

Windowed output on Windows, macOS and any Linux desktop, including a Pi running X11 or Wayland.

```csharp
using var backend = new SdlBackend(1024, 600, "My application", vsync: true);
```

Each flushed region goes into a streaming `ARGB8888` texture; the texture is presented once per
frame, on the last region. Because the texture keeps its contents between frames, LVGL's partial
redraw mode is correct here — only changed strips are uploaded.

`SdlBackend` also implements `ILvglKeyboardSource`, so keyboard input works when
`LvglOptions.EnableKeyboard` is set. SDL keycodes are mapped onto LVGL's `LV_KEY_*` navigation
codes; printable ASCII passes through unchanged.

If no accelerated renderer is available — headless CI, some Pi configurations — it falls back to
SDL's software renderer automatically.

## Linux framebuffer — `LVGL.Net.Linux`

Direct output to `/dev/fb0` with no window system. This is the backend for a Pi driving a panel as
an instrument or kiosk.

```csharp
var pointer = EvdevPointer.AutoDetect(screenWidth, screenHeight);
using var backend = new FramebufferBackend("/dev/fb0", pointer);
```

It memory-maps the framebuffer and copies each flushed region into it, so a redraw is one `memcpy`
per scan line. 32-bit and 16-bit devices are supported; the red/blue channel order is read from the
driver and swapped only when it differs from what LVGL produces.

`EvdevPointer` reads `/dev/input/eventN` and handles both device kinds: touch panels report
absolute positions, rescaled using the range the driver advertises through `EVIOCGABS`, and mice
report relative motion, which is accumulated and clamped.

Both need group membership:

```bash
sudo usermod -aG video,input $USER    # then log out and back in
```

## DRM/KMS — `LVGL.Net.Linux`

Current Raspberry Pi OS drives displays through the kernel's KMS driver (`vc4-kms-v3d`); the
`/dev/fb0` device the framebuffer backend uses is an emulation on top of it, which can only copy
into the image being shown and therefore tears on movement. `DrmBackend` talks to KMS directly:

```csharp
var pointer = EvdevPointer.AutoDetect(800, 480);
using var backend = new DrmBackend(pointer: pointer);   // first card with a connected display
Console.WriteLine($"{backend.DevicePath}: {backend.Mode}");
```

It allocates two scan-out buffers and flips between them at vertical blank. LVGL renders strips
into a shadow copy of the screen; at the end of a frame the damaged region is copied into the hidden
buffer and a page flip is queued. The hidden buffer last showed the frame *before* the previous one,
so the copy covers this frame's damage and the last frame's together - small updates stay small and
neither buffer is ever stale. The wait for the flip happens at the start of the next frame, so LVGL
renders while the display catches up.

- **No libdrm.** The ioctls are declared directly; their argument structures are fixed-width kernel
  ABI, identical on arm, arm64 and x64, and pinned by unit tests.
- **The display must be free.** Only one program may drive it: boot to the console, or stop the
  desktop on that display. With a desktop running, use the SDL backend instead.
- **Fallback.** If the driver refuses page flips, the backend draws into the visible buffer and
  reports `IsDoubleBuffered == false` rather than stopping.
- The console's mode is restored when the backend is disposed.

The Pi sample uses it with `--drm`.

## Headless — in `LVGL.Net`

Renders into a managed `byte[]`. Three things use it: the designer's live preview, which copies the
buffer into a bitmap; tests, which can assert on pixels without a display server; and screenshots -
`SavePng` writes exactly what LVGL drew.

```csharp
using var backend = new HeadlessBackend(800, 480);
using var app = LvglApplication.Create(backend, new LvglOptions { EnablePointer = false });

app.RunFrame();

if (backend.IsDirty)
{
    ReadOnlySpan<byte> frame = backend.Frame;   // XRGB8888, stride = Width * 4
    backend.ClearDirty();
}

backend.SetPointer(120, 80, pressed: true);     // synthetic input

app.Display.RefreshNow();                       // render everything pending, right now
backend.SavePng("screen.png");
```

## Writing your own

The whole contract is four members. A backend for a SPI display, an e-paper panel or a remote
frame stream is a single class - `DrmBackend` is a worked example of a double-buffered one.

```csharp
public sealed class MyBackend : ILvglBackend
{
    public int Width => 320;
    public int Height => 240;
    public LvPointerState Pointer { get; private set; }

    public void Blit(in LvArea area, nint pixels, bool isLastFlush)
    {
        // `pixels` is XRGB8888, tightly packed, stride = area.Width * 4.
        // Valid only for the duration of the call - copy, do not retain.
        // `area` bounds are inclusive: a 1x1 region has X1 == X2.

        if (isLastFlush) Present();
    }

    public bool PumpEvents()
    {
        Pointer = ReadTouchPanel();
        return true;
    }

    public void Dispose() { }
}
```

Three rules:

- **`isLastFlush` is when to present.** A device with a swap chain or double buffering should flip
  there, not on every region.
- **Do not retain `pixels`.** The pointer is into LVGL's draw buffer, which is reused immediately.
- **Throwing is allowed.** The exception is captured and rethrown from the run loop rather than
  unwinding into C.

Implement `ILvglKeyboardSource` as well if the device has keys.

## Render modes

| Mode | Buffers | Use for |
|------|---------|---------|
| `Partial` (default) | Two strips of `PartialBufferLines` scan lines | Embedded targets; lowest memory |
| `Direct` | Two full-screen buffers | Fewer, larger blits when memory is plentiful |
| `Full` | Two full-screen buffers, redrawn entirely | Devices that cannot do partial updates |

```csharp
var app = LvglApplication.Create(backend, new LvglOptions
{
    RenderMode = LvRenderMode.Partial,
    PartialBufferLines = 32,     // 0 = one tenth of the screen height
});
```

`app.Display.AllocatedBytes` reports what the choice actually cost.
