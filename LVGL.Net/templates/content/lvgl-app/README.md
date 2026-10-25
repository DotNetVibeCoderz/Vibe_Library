# Company.LvglTemplateApp

An [LVGL.Net](https://github.com/DotNetVibeCoderz/Vibe_Library/tree/main/LVGL.Net) application:
an LVGL user interface in .NET that runs in a desktop window while you develop it and on a
Raspberry Pi display when you deploy it.

## Run it on your computer

```bash
dotnet run
```

The desktop window needs SDL2: `sudo apt install libsdl2-2.0-0` (Debian, Ubuntu, Raspberry Pi OS),
`brew install sdl2` (macOS), or `SDL2.dll` from the
[SDL releases](https://github.com/libsdl-org/SDL/releases) next to the built executable (Windows).

Without a display at all:

```bash
dotnet run -- --headless --screenshot screen.png    # render off-screen and save a PNG
```

## Run it on a Raspberry Pi

```bash
sudo usermod -aG video,input $USER                  # once; then log out and back in
dotnet run -- --drm                                 # KMS page flipping: no tearing
dotnet run -- --fb                                  # or the framebuffer, /dev/fb0
```

`--drm` needs the display to itself - boot to the console, or stop the desktop. With a desktop
running, plain `dotnet run` opens a window instead.

To deploy without the SDK on the Pi:

```bash
dotnet publish -c Release -r linux-arm64 --self-contained
# copy bin/Release/net10.0/linux-arm64/publish/ to the Pi and run ./Company.LvglTemplateApp --drm
```

The LVGL.Net package carries the native LVGL library for every runtime, so nothing has to be
compiled on the Pi.

## Where things are

| File | What it is |
|---|---|
| `Program.cs` | Picks the backend (window, DRM, framebuffer, headless) and runs the loop |
| `MainScreen.cs` | The first screen |
<!--#if (UseJson) -->
| `Screens/MainScreen.lvgl.json` | Its layout. Open it in the LVGL.Net designer to edit it visually |

The layout is loaded at run time, so changing it needs no rebuild of the code - only of the copy in
the output folder (`dotnet build` does that). Widgets are found by name with `builder.Find<T>(...)`.
<!--#endif -->

Add more screens with `dotnet new lvgl-screen -n Settings`.

## Rules worth knowing

- **One thread.** LVGL is single-threaded. Touch widgets on the thread that created the
  application; from anywhere else use `app.Post(...)`. `LvTimer` callbacks already run on it.
- **Widgets are not disposed.** LVGL owns the tree - `Delete()` a widget, or `Clear()` a parent.
- **Styles, fonts and images outlive their widgets.** Delete the widgets first, then dispose the
  `LvStyle`/`LvFont`/`LvImageSource` they used. `MainScreen.Destroy` does this in that order.
- **No arithmetic on `LvCoord.Percent(...)`.** The flag lives in the high bits; compute in pixels.

Documentation: [English](https://github.com/DotNetVibeCoderz/Vibe_Library/tree/main/LVGL.Net/docs/en) ·
[Bahasa Indonesia](https://github.com/DotNetVibeCoderz/Vibe_Library/tree/main/LVGL.Net/docs/id)
