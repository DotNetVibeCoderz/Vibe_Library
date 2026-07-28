# LVGL.Net documentation (English)

> Dokumentasi Bahasa Indonesia: [`docs/id`](../id/README.md)
>
> **Ported by Kang Fadhil of Gravicode Studios** — *Di port oleh Kang Fadhil dari Gravicode Studios.*

LVGL.Net is a multi-platform .NET 10 wrapper around [LVGL](https://github.com/lvgl/lvgl) v9, the
embedded graphics library, together with a WPF visual designer and two sample applications.

| # | Document | What it covers |
|---|----------|----------------|
| 1 | [Getting started](01-getting-started.md) | Prerequisites, first build, first window |
| 2 | [Architecture](02-architecture.md) | How the managed and native halves fit together, and why |
| 3 | [Building the native library](03-native-build.md) | CMake, the ABI shim, `lv_conf.h`, cross-compiling |
| 4 | [Widgets and styling](04-widgets.md) | The widget API, events, layout, styles |
| 5 | [Backends](05-backends.md) | SDL, Linux framebuffer, headless, writing your own |
| 6 | [The designer](06-designer.md) | Editing layouts, live preview, C# export |
| 7 | [Raspberry Pi guide](07-raspberry-pi.md) | Deploying to a Pi 4, permissions, autostart |
| 8 | [Troubleshooting](08-troubleshooting.md) | The failures you are most likely to hit |
| 9 | [The design assistant](09-assistant.md) | Jack The Code Bender: providers, tools, attachments, prompts |
| 10 | [The designer workspace](10-designer-workspace.md) | Design/Code modes, the code editor, the docked assistant panel |

## Repository layout

```
src/LVGL.Net           Core wrapper - platform-agnostic, no UI framework dependency
src/LVGL.Net.Sdl       SDL2 backend (Windows, macOS, Linux desktop)
src/LVGL.Net.Linux     Framebuffer + evdev backend (Raspberry Pi, kiosk devices)
src/LVGL.Net.Ui        Serializable layout model, runtime loader, C# generator
src/LVGL.Net.Assistant Jack The Code Bender - Semantic Kernel assistant, providers, tools
tools/LVGL.Designer    WPF visual designer (Windows only, by nature of WPF)
samples/LVGL.Desktop   Cross-platform component demo browser
samples/LVGL.Raspi     Live sensor dashboard for a Raspberry Pi 4
tests/LVGL.Net.Tests   Unit tests - run without the native library
native/                LVGL build, ABI shim, lv_conf.h, build scripts
```

## Quick reference

```bash
./native/build.sh                                  # build the native library (Linux/macOS)
./native/build.ps1                                 # build the native library (Windows)
dotnet build LVGL.Net.slnx                         # build everything managed
dotnet test  LVGL.Net.slnx                         # run the tests
dotnet run --project samples/LVGL.Desktop          # component demos
dotnet run --project samples/LVGL.Raspi -- --sdl   # sensor dashboard in a window
dotnet run --project tools/LVGL.Designer           # visual designer (Windows)
```
