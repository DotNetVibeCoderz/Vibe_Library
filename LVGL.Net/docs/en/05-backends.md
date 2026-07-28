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

## Headless — in `LVGL.Net`

Renders into a managed `byte[]`. Two things use it: the designer's live preview, which copies the
buffer into a `WriteableBitmap`, and tests, which can assert on pixels without a display server.

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
```

## Writing your own

The whole contract is four members. A backend for a SPI display, a DRM/KMS device or a remote
frame stream is a single class.

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
