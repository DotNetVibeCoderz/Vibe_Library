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

The managed assemblies and the native library are from different commits - typically a library
built before the move to LVGL 9.6 (shim ABI 3) next to newer managed code (ABI 4). Rebuild the
native side; if CMake complains that its cache belongs to another directory, rebuild clean
(`./native/build.ps1 -Clean`, or delete `native/build`).

## "The native library was built with LV_COLOR_DEPTH=16"

LVGL.Net requires 32-bit colour: the backends blit raw XRGB8888 words, and a 16-bit build would
render garbled output. Restore `#define LV_COLOR_FORMAT_DEFAULT LV_COLOR_FORMAT_XRGB8888` in
`native/lv_conf.h` and rebuild.

## Integration tests all "pass" in a fraction of a second

They skipped: without a loadable native library every integration test returns early. Set
`LVGLNET_REQUIRE_NATIVE=1` and the run fails instead, with the reason the library did not load.

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

## A chart's grid turned bright white, or every sample has a dot

Both changed in LVGL 9.5. The grid lines now use the **main** part's line style - colour them with
`chart.SetLineColor(color)` rather than `LvPart.Items` - and point markers are drawn by default:
`chart.SetPartSize(0, 0, LvPart.Indicator)` removes them.

## The chart does not scroll

`UpdateMode` must be `Shift` for a rolling window. `Circular` overwrites in place, like an
oscilloscope sweep.

```csharp
chart.UpdateMode = LvChartUpdateMode.Shift;
```

## The designer says "Preview offline" but the app still works

Expected when the native library is missing — the panel on the board says so, and the status bar
shows *LVGL offline*. Editing, saving and C# export do not need LVGL.

## An image or font does not show

`UiBuilder.Diagnostics` (and the designer's *things to check*) lists every asset it could not find.
Asset paths in a layout are relative to the layout file; in code, `LvImageSource.FromFile` and
`LvFont.FromFile` throw `FileNotFoundException` before LVGL sees the path. If a PNG still does not
render, check it really is PNG or BMP - those are the decoders compiled in.

## `DrmBackend`: "DRM_IOCTL_MODE_SETCRTC failed"

Another program owns the display - usually the desktop. Boot to the console or stop the display
manager, or use the SDL backend while a desktop is running. "No DRM device has a connected display"
means KMS is off (`dtoverlay=vc4-kms-v3d` in `/boot/firmware/config.txt`) or the panel is not
connected; the framebuffer backend still works in that case.

## An event handler runs but its exception disappears

A managed exception must not unwind into LVGL's C stack, so it is caught at the boundary. It is not
lost: subscribe to `LvglRuntime.CallbackFailed` to log it. Timers and animations report there too.

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
