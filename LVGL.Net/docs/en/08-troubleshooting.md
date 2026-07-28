# Troubleshooting

## `LvglNativeNotFoundException`

The native library has not been built, or was built somewhere the resolver does not look. The
exception lists every path it tried.

```bash
./native/build.sh          # or ./native/build.ps1 on Windows
```

To use a library from elsewhere:

```bash
export LVGLNET_NATIVE_PATH=/opt/lvglnet/liblvglnet.so    # file or containing directory
```

## "Native shim ABI *n* does not match the expected *m*"

The managed assemblies and the native library are from different commits. Rebuild the native side.

## "The native library was built with LV_COLOR_DEPTH=16"

LVGL.Net requires 32-bit colour: the backends blit raw XRGB8888 words, and a 16-bit build would
render garbled output. Restore `#define LV_COLOR_DEPTH 32` in `native/lv_conf.h` and rebuild.

## `SDL_Init failed` / SDL2 not found

```bash
sudo apt install libsdl2-2.0-0        # Debian, Ubuntu, Raspberry Pi OS
brew install sdl2                     # macOS
```

On Windows, put `SDL2.dll` from the [SDL releases](https://github.com/libsdl-org/SDL/releases) into
`runtimes/win-x64/native/`. Note the DLL is not checked in — `.gitignore` excludes binaries in
`runtimes/` — so a fresh clone needs this step again.

Over SSH, SDL needs a display: `ssh -X`, or use the framebuffer backend instead.

## `Could not open /dev/fb0` — permission denied

```bash
sudo usermod -aG video,input $USER    # then log out and back in
groups                                # confirm 'video' and 'input' are listed
```

## `LvglThreadAccessException`

A widget was touched from a thread that does not own LVGL. LVGL keeps its state in globals with no
locking, so this is caught rather than left to corrupt memory.

```csharp
// wrong - on a background thread
label.Text = reading.ToString();

// right
app.Post(() => label.Text = reading.ToString());
```

`Post` is fire-and-forget; `Invoke` blocks until the action has run.

## `ObjectDisposedException` from a widget property

The widget was deleted — directly, or because a parent or `Clear()` took it with it. Check
`IsAlive` when a reference may outlive its widget:

```csharp
if (_readout is { IsAlive: true }) _readout.Text = message;
```

## A layout is positioned wrongly after using `LvCoord.Percent`

Arithmetic on an encoded coordinate is the usual cause. LVGL stores the percentage flag in the high
bits, so `LvCoord.Percent(100) - 190` is not "100% minus 190 pixels" — it is a corrupted value that
happens to look like a plain pixel count.

```csharp
// wrong
panel.SetSize(LvCoord.Percent(100) - SidebarWidth, LvCoord.Percent(100));

// right
panel.SetSize(app.Display.Width - SidebarWidth, app.Display.Height);
```

## Nothing renders, or the window is black

- Is anything parented to the active screen? `new LvLabel(app.Screen, "test")` is the quickest
  check.
- Is `app.Run()` (or a `RunFrame()` loop) actually running?
- On the framebuffer, is another process — a desktop session, another instance — also drawing?

## Colours are wrong (red and blue swapped)

On a custom backend, the source is XRGB8888: bytes B, G, R, X on little-endian. The framebuffer
backend detects the device's channel order and swaps when needed; a hand-written backend has to
match its own target.

## The chart does not scroll

`UpdateMode` must be `Shift` for a rolling window. `Circular` overwrites in place, like an
oscilloscope sweep.

```csharp
chart.UpdateMode = LvChartUpdateMode.Shift;
```

## The designer preview is blank but the app still works

Expected when the native library is missing — the panel over the preview says so. Editing, saving
and C# export do not need LVGL.

## An event handler runs but its exception disappears

Deliberate: a managed exception must not unwind into LVGL's C stack, so handlers swallow theirs.
Catch and log inside the handler.

## High CPU when idle

Check `MinIdleSleepMs` is not zero — zero busy-waits. The default of 1 ms yields instead. If the
UI redraws constantly, look for an animation or a timer invalidating widgets every frame;
`app.Display.FlushCount` rising steadily on a static screen confirms it.

## Diagnostics

```csharp
LvglRuntime.LvglVersion            // the LVGL build actually loaded
LvglRuntime.ColorDepth             // must be 32
app.Display.AllocatedBytes         // draw buffer memory
app.Display.FlushCount             // regions flushed since startup
app.FrameCount
LvglNativeLibrary.ProbedPaths      // where the resolver looked
```

Turn on LVGL's own logging by setting `LV_USE_LOG` to 1 in `native/lv_conf.h` and rebuilding.
