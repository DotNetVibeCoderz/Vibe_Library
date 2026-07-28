# Getting started

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| .NET SDK 10.0 | `dotnet --version` should report 10.x |
| CMake 3.20+ and a C compiler | Only needed to build the native library |
| Git | The native build clones LVGL v9 |
| SDL2 | Only for the SDL backend (desktop windows) |

On Windows you probably already have the C toolchain: a Visual Studio install with the *Desktop
development with C++* workload ships CMake, Ninja and MSVC. `native/build.ps1` locates them itself,
so there is nothing to add to `PATH` and no Developer Command Prompt needed.

Installing SDL2:

```bash
sudo apt install libsdl2-2.0-0        # Debian, Ubuntu, Raspberry Pi OS
brew install sdl2                     # macOS
```

Windows has no system SDL2. Download `SDL2-<version>-win32-x64.zip` from the
[SDL releases](https://github.com/libsdl-org/SDL/releases) and put `SDL2.dll` in
`runtimes/win-x64/native/`, beside `lvglnet.dll` — the backend searches the same probe paths as the
core library, so nothing needs to go on `PATH`:

```powershell
$url = 'https://github.com/libsdl-org/SDL/releases/download/release-2.32.10/SDL2-2.32.10-win32-x64.zip'
Invoke-WebRequest $url -OutFile sdl2.zip
Expand-Archive sdl2.zip -DestinationPath sdl2 -Force
Copy-Item sdl2/SDL2.dll runtimes/win-x64/native/
```

The Linux framebuffer backend needs no SDL at all — that is the point of it.

## Build the native library

LVGL is a C library, so there is one compilation step before any of the managed code can run:

```bash
./native/build.sh          # Linux, macOS
./native/build.ps1         # Windows
```

This fetches LVGL v9 into `native/lvgl`, compiles it together with the ABI shim in `native/shim`,
and stages the result in `runtimes/<rid>/native/`. The managed projects pick it up from there
automatically — nothing needs to be copied by hand.

You only need to repeat this when you change `native/lv_conf.h`, change the shim, or move to a
different LVGL version.

## Build and test the managed code

```bash
dotnet build LVGL.Net.slnx
dotnet test  LVGL.Net.slnx
```

The tests deliberately do **not** require the native library, so `dotnet test` works on a machine
without CMake. Tests that would need LVGL check for it and return early.

## Your first window

```csharp
using Lvgl;
using Lvgl.Sdl;
using Lvgl.Widgets;

using var backend = new SdlBackend(480, 320, "Hello LVGL");
using var app = LvglApplication.Create(backend);

var label = new LvLabel(app.Screen, "Hello from .NET");
label.Align(LvAlign.TopMid, 0, 40);
label.SetFontSize(20);

var button = new LvButton(app.Screen, "Tap me");
button.SetSize(160, 48);
button.Center();
button.Clicked += (_, _) => label.Text = $"Clicked at {DateTime.Now:HH:mm:ss}";

app.Run();
```

Three things are worth noticing:

- **`LvglApplication.Create` claims the calling thread.** LVGL keeps its state in globals with no
  locking, so exactly one thread may touch it. Work arriving from elsewhere goes through
  `app.Post(...)`.
- **Widgets are not disposed.** LVGL owns the object tree; deleting a parent deletes its children.
  Call `Delete()` when you really want a widget gone.
- **`app.Run()` blocks** until the window is closed, `Stop()` is called, or the cancellation token
  fires.

## Run the samples

```bash
dotnet run --project samples/LVGL.Desktop                    # component browser
dotnet run --project samples/LVGL.Desktop -- --width 1280 --height 720
dotnet run --project samples/LVGL.Raspi -- --sdl --simulate  # dashboard, fake sensors, in a window
```

To check the whole stack without watching a window — useful in CI — render a fixed number of
frames and exit:

```bash
dotnet run --project samples/LVGL.Desktop -- --frames 120 --no-vsync
# LVGL 9.2.2  |  1024x600  |  draw buffers 480 KiB
# Rendered 120 frames in 224 ms (14 region flushes)
```

A non-zero exit code means no region was ever flushed, i.e. nothing rendered.

## Next

- [Architecture](02-architecture.md) — why the wrapper is shaped the way it is
- [Widgets and styling](04-widgets.md) — the day-to-day API
