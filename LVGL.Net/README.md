# LVGL.Net

> **Di port oleh Kang Fadhil dari Gravicode Studios.**
> *Ported by Kang Fadhil of Gravicode Studios.*

A multi-platform .NET 10 wrapper for [LVGL](https://github.com/lvgl/lvgl) v9.6, the embedded
graphics library — plus a cross-platform visual designer with an AI design assistant, a component
demo browser, and a live sensor dashboard for the Raspberry Pi 4.

Documentation: **[English](docs/en/README.md)** · **[Bahasa Indonesia](docs/id/README.md)** ·
[API reference](docs/api/README.md)

![The LVGL.Net designer](https://raw.githubusercontent.com/DotNetVibeCoderz/Vibe_Library/main/LVGL.Net/docs/images/screenshots/designer-design.png)

## What is here

| Project | Purpose |
|---------|---------|
| `src/LVGL.Net` | The core wrapper. Platform-agnostic, no NuGet dependencies, no UI framework |
| `src/LVGL.Net.Sdl` | SDL2 backend — windowed output on Windows, macOS and Linux |
| `src/LVGL.Net.Linux` | Linux backends — framebuffer, DRM/KMS with page flipping, evdev input |
| `src/LVGL.Net.Ui` | Serializable layout model, runtime loader and C# code generator |
| `src/LVGL.Net.Assistant` | Jack The Code Bender — Semantic Kernel assistant (OpenAI, Anthropic, Gemini, Ollama) |
| `tools/LVGL.Designer` | Avalonia visual designer with a live LVGL preview and the AI assistant |
| `samples/LVGL.Desktop` | Demo browser for the widget, layout, styling and animation APIs |
| `samples/LVGL.Raspi` | Raspberry Pi telemetry dashboard — CPU, temperature, memory, live chart, bench mode |
| `tests/` | Library and designer tests; the unit tests run without the native library |
| `benchmarks/` | BenchmarkDotNet measurements of the render path |
| `native/` | LVGL build, ABI shim, `lv_conf.h`, build scripts |

<table>
<tr>
<td><img src="https://raw.githubusercontent.com/DotNetVibeCoderz/Vibe_Library/main/LVGL.Net/docs/images/screenshots/desktop-02-controls.png" alt="Desktop sample: controls" /></td>
<td><img src="https://raw.githubusercontent.com/DotNetVibeCoderz/Vibe_Library/main/LVGL.Net/docs/images/screenshots/raspi-dashboard.png" alt="Raspberry Pi dashboard" /></td>
</tr>
<tr>
<td align="center">Tabs, a scale-and-arc gauge, LEDs, switches, a spinner and a timer-driven clock</td>
<td align="center">The Pi dashboard, rendered by LVGL off-screen</td>
</tr>
</table>

## Install

```bash
dotnet add package LVGL.Net          # core wrapper, ships the native lvglnet for every RID
dotnet add package LVGL.Net.Sdl      # windowed output on Windows, macOS and Linux
dotnet add package LVGL.Net.Linux    # framebuffer, DRM/KMS and evdev, no window system
dotnet add package LVGL.Net.Ui       # layout document model, loader and C# generator
```

`LVGL.Net.Sdl` needs SDL2 itself on the machine — the package does not redistribute it. Install
`libsdl2-2.0-0` on Debian/Ubuntu, `sdl2` via Homebrew, or drop `SDL2.dll` into
`runtimes/win-x64/native/` on Windows.

## Build from source

```bash
./native/build.sh                            # Linux/macOS - builds LVGL + the shim
./native/build.ps1                           # Windows

dotnet build LVGL.Net.slnx
dotnet test  LVGL.Net.slnx

dotnet run --project samples/LVGL.Desktop    # needs SDL2
dotnet run --project tools/LVGL.Designer     # the designer, on any desktop OS
```

```csharp
using var backend = new SdlBackend(480, 320, "Hello LVGL");
using var app = LvglApplication.Create(backend);

var button = new LvButton(app.Screen, "Tap me");
button.SetSize(160, 48);
button.Center();
button.Clicked += (_, _) => Console.WriteLine("clicked");

button.AnimateOpacity(from: 0, to: 255, durationMs: 300);

app.Run();
```

## The widget set

Label, Button, Slider, Bar, Arc, Switch, Checkbox, Dropdown, Roller, TextArea and Chart; Image
(PNG, BMP, run-time pixels, symbols); TabView, TileView, List and MessageBox; an on-screen Keyboard;
Spinner, LED, Scale, Table and Spinbox — with shared styles, TrueType fonts, animations and timers.
Every layout widget is known to the whole stack: the wrapper, the layout document, the runtime
builder, the C# generator, the designer and the assistant.

## Design notes

**The native library contains no display drivers.** LVGL renders into a buffer and calls a flush
callback; everything after that — SDL window, Linux framebuffer, DRM/KMS page flipping, off-screen
preview — is managed code behind a four-member `ILvglBackend`. One native artifact serves every
target, adding an output device is a C# class, and the library links nothing but libc and libm.

**A small C shim covers what P/Invoke cannot do safely.** `lv_color_t` is a three-byte struct whose
by-value ABI differs between Windows x64 and SysV; `lv_style_t`, `lv_anim_t` and `lv_image_dsc_t`
are layouts managed code should not hard-code; LVGL's fonts are exported variables; and enum
ordinals move between releases — LVGL 9.5 inserted `LV_STATE_ALT` at bit 0 and shifted every other
state. Each of those goes through an `lvn_*` entry point, and a test compares every managed enum
mirror with the loaded build, so the next shift is a red build rather than a silent misbehaviour.

**Built for constrained hardware.** Partial render mode by default, unmanaged draw buffers,
unmanaged function pointers instead of marshalled delegates, workstation GC. The render path
allocates nothing — a test redraws 150 frames with a streaming chart, an animation and a timer and
requires exactly zero managed bytes. A full 800 × 480 redraw measures 0.54 ms on a laptop CPU, and
the Pi dashboard runs on 200 KiB of draw buffers.

**One layout model, three consumers.** The designer edits a `UiDocument`, `UiBuilder` instantiates
it at run time, and `CSharpUiGenerator` emits equivalent C# — which the tests compile, for every
widget kind. A previewed layout and a running one cannot drift apart.

**The designer shows LVGL's own pixels.** Its board is a real LVGL instance rendering off-screen;
widgets are moved and resized on it directly, and drawn as component footprints with reference
designators (`BTN2`, `LBL1`) like parts on a PCB.

**The assistant validates before it answers.** Jack designs layouts through a tool that checks
them against the real document model, so a malformed layout comes back as problems to fix, and a
valid one lands on the canvas as an undoable step. Anthropic goes through the official SDK, which
handles Claude's removed sampling parameters and workspace-scoped keys.

## Requirements

- .NET SDK 10.0
- CMake 3.20+ and a C compiler, to build the native library
- SDL2, only for the SDL backend

## Status

See [PLAN.md](PLAN.md) for direction and [Progress.md](Progress.md) for what is done and how each
part was verified.

## License

LVGL is MIT-licensed. This wrapper follows suit.
