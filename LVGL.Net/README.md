# LVGL.Net

> **Di port oleh Kang Fadhil dari Gravicode Studios.**
> *Ported by Kang Fadhil of Gravicode Studios.*

A multi-platform .NET 10 wrapper for [LVGL](https://github.com/lvgl/lvgl) v9, the embedded graphics
library — plus a WPF visual designer with an AI design assistant, a cross-platform component demo,
and a live sensor dashboard for the Raspberry Pi 4.

Documentation: **[English](docs/en/README.md)** · **[Bahasa Indonesia](docs/id/README.md)**

## What is here

| Project | Purpose |
|---------|---------|
| `src/LVGL.Net` | The core wrapper. Platform-agnostic, no NuGet dependencies, no UI framework |
| `src/LVGL.Net.Sdl` | SDL2 backend — windowed output on Windows, macOS and Linux |
| `src/LVGL.Net.Linux` | Framebuffer + evdev backend — direct to `/dev/fb0`, no window system |
| `src/LVGL.Net.Ui` | Serializable layout model, runtime loader and C# code generator |
| `src/LVGL.Net.Assistant` | Jack The Code Bender — Semantic Kernel assistant (OpenAI, Anthropic, Gemini, Ollama) |
| `tools/LVGL.Designer` | WPF visual designer with a live LVGL preview and the AI assistant |
| `samples/LVGL.Desktop` | Demo browser for the widget, layout and styling APIs |
| `samples/LVGL.Raspi` | Raspberry Pi telemetry dashboard — CPU, temperature, memory, live chart |
| `tests/LVGL.Net.Tests` | Unit tests; run without the native library |
| `native/` | LVGL build, ABI shim, `lv_conf.h`, build scripts |

## Quick start

```bash
./native/build.sh                            # Linux/macOS - builds LVGL + the shim
./native/build.ps1                           # Windows

dotnet build LVGL.Net.slnx
dotnet test  LVGL.Net.slnx

dotnet run --project samples/LVGL.Desktop    # needs SDL2
```

```csharp
using var backend = new SdlBackend(480, 320, "Hello LVGL");
using var app = LvglApplication.Create(backend);

var button = new LvButton(app.Screen, "Tap me");
button.SetSize(160, 48);
button.Center();
button.Clicked += (_, _) => Console.WriteLine("clicked");

app.Run();
```

## Design notes

**The native library contains no display drivers.** LVGL renders into a buffer and calls a flush
callback; everything after that — SDL window, Linux framebuffer, off-screen preview — is managed
code behind a four-member `ILvglBackend`. One native artifact serves every target, adding an output
device is a C# class, and the library links nothing but libc and libm.

**A small C shim covers what P/Invoke cannot do safely.** `lv_color_t` is a three-byte struct whose
by-value ABI differs between Windows x64 and SysV; `lv_style_t`'s size is a compile-time detail;
LVGL's fonts are exported variables; and `lv_event_code_t` ordinals shifted during the 9.x series.
Each of those goes through an ABI-stable `lvn_*` entry point. Everything else calls `lv_*` directly,
and the render path does not touch the shim at all.

**Built for constrained hardware.** Partial render mode by default, unmanaged draw buffers,
unmanaged function pointers instead of marshalled delegates on the hot path, workstation GC, and no
allocation per chart sample. The Raspberry Pi sample runs on ~100 KiB of draw buffers at 800×480.

**One layout model, three consumers.** The designer edits a `UiDocument`, `UiBuilder` instantiates
it at run time, and `CSharpUiGenerator` emits equivalent C#. A previewed layout and a running one
cannot drift apart.

**The assistant validates before it answers.** Jack The Code Bender designs layouts through a
kernel function that checks them against the real document model, so a malformed layout comes back
as fixable problems rather than as plausible JSON that will not open. Anthropic goes through the
official SDK rather than an OpenAI-compatible shim — Claude removed the sampling parameters on its
current models, and the connector drops a configured temperature for those rather than letting the
request fail.

## Requirements

- .NET SDK 10.0
- CMake 3.20+ and a C compiler, to build the native library
- SDL2, only for the SDL backend

## Status

The managed solution builds clean and its tests pass. The native library must be built once with
CMake before anything renders; the wrapper reports a clear, actionable error until then, and the
designer keeps working without it for everything except the preview.

## License

LVGL is MIT-licensed. This wrapper follows suit.
