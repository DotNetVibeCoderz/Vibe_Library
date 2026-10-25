# LVGL.Net — Roadmap / Rencana Pengembangan

Di port oleh Kang Fadhil dari Gravicode Studios.

This file sets **direction**: what LVGL.Net is for, and what comes next. **Status**, meaning what
is built and how it was verified, lives in [Progress.md](Progress.md).

Berkas ini berisi **arah**: untuk apa LVGL.Net dibuat dan apa langkah berikutnya. **Status**
(apa yang sudah dibangun dan bagaimana diverifikasi) ada di [Progress.md](Progress.md).

## Purpose / Tujuan

A .NET 10 wrapper for LVGL v9 that runs the same UI code on a desktop window and on a Raspberry Pi
display, light enough for a 1 GB board. One native artifact per RID with no display drivers;
every output device is a managed `ILvglBackend`. Around it: a cross-platform Avalonia designer with
an AI assistant, a desktop demo browser and a Pi sensor dashboard.

Wrapper .NET 10 untuk LVGL v9 yang menjalankan kode UI yang sama di jendela desktop dan di layar
Raspberry Pi, cukup ringan untuk board 1 GB. Satu artefak native per RID tanpa driver layar;
setiap perangkat keluaran adalah `ILvglBackend` di sisi managed. Di sekelilingnya: designer
Avalonia lintas platform dengan asisten AI, browser demo desktop, dan dasbor sensor Pi.

## Principles / Prinsip

These hold across every milestone. A feature that needs to break one is redesigned, not merged.

Prinsip ini berlaku di setiap tahap. Fitur yang harus melanggarnya dirancang ulang, bukan digabung.

1. **No drivers in the native build.** New outputs are backends (see CLAUDE.md).
   *Tidak ada driver di build native; keluaran baru adalah backend.*
2. **Nothing ABI-fragile crosses P/Invoke.** `lv_color_t` by value, struct layouts, font globals,
   event ordinals and enum values go through `lvn_*` or are cross-checked against the build; any
   shim change bumps `LVN_ABI_VERSION` and `ExpectedShimAbi` together.
   *Apa pun yang rapuh secara ABI lewat shim `lvn_*` atau diperiksa silang terhadap build.*
3. **No allocation on the render path.** Measured, not assumed - a test enforces it.
   *Tanpa alokasi di jalur render - ditegakkan oleh test.*
4. **The core stays dependency-free.** No NuGet packages, no UI framework in `LVGL.Net`.
   *Core tanpa dependensi.*
5. **A widget is done only when the whole stack knows it:** wrapper, `UiWidgetType`, `UiBuilder`,
   `CSharpUiGenerator`, the designer's inspector, the assistant's validator, and docs in both
   languages.
   *Widget dianggap selesai hanya jika seluruh tumpukan mengenalnya.*

## Milestones / Tahapan

### 0.1.x: delivered / sudah dikirim
- Core wrapper over LVGL v9.2.2: 13 widgets, styles, groups and focus, input devices, stable events
- Backends: SDL2, Linux framebuffer + evdev, headless
- `LVGL.Net.Ui` model, JSON, builder, C# generator; WPF designer with Jack The Code Bender
- `LVGL.Desktop` and `LVGL.Raspi` samples; native builds for six RIDs; five NuGet packages

### 0.2: widget coverage / cakupan widget — delivered / sudah dikirim
- `LvImage` (PNG/BMP files decoded natively, run-time pixels, symbols), `LvAnimation`, `LvTimer`
- `LvTabView`, `LvList`, `LvMessageBox`, `LvTileView`, `LvKeyboard`
- `LvSpinner`, `LvLed`, `LvScale`, `LvTable`, `LvSpinbox`
- `LvglRuntime.CallbackFailed`: exceptions in callbacks are contained *and* visible

### 0.3: designer / designer — delivered / sudah dikirim
- **Ported to Avalonia** - the designer now runs on Windows, macOS and Linux
- Direct manipulation on the board: select, drag, resize pads, grid snapping, undo/redo
- Nesting in every container, with tab/tile structure enforced; drag to re-parent in the outline
- Shared styles, image and font assets, screen-size presets

### 0.4: platform and hardware / platform dan perangkat keras — built, partly verified / dibangun, sebagian terverifikasi
- LVGL 9.6.0, with every managed enum cross-checked against the build
- TrueType fonts at run time (TinyTTF)
- `DrmBackend`: KMS page flipping without libdrm - **still needs a run on a real display**
- macOS in the CI test matrix; a hardware-in-the-loop workflow and `--bench` for the Pi -
  **still needs a self-hosted Pi 4 runner to be registered**

### Later items / Butir "Nanti" — delivered / sudah dikirim
- Allocation benchmarks (BenchmarkDotNet) and a zero-allocation test
- Generated API reference (`docs/api`), kept fresh by a test

### Templates and gallery / Template dan galeri — delivered / sudah dikirim
- `dotnet new lvgl` (C# or designer-layout UI) and `dotnet new lvgl-screen`, in `LVGL.Net.Templates`
  *Template proyek `dotnet new` untuk memulai aplikasi dan menambah layar.*
- `LVGL.Gallery`: seventeen use-case demos - scientific, work and data on SQLite, multimedia,
  games, tools, home/IoT - each showing its own source
  *Galeri kasus penggunaan, masing-masing dengan kode sumbernya.*
- In the core for it: `LvCanvas`, key codes, swipe direction, pointer position, label recolouring,
  scrolling, centre-zero arcs

### 0.5: next / berikutnya
- **Close the hardware gap.** Register the Pi 4 runner, run the HIL workflow, and put the Pi's own
  bench numbers and a photo of `DrmBackend` on a panel into the docs.
  *Jalankan uji di Pi 4 sungguhan dan catat angkanya.*
- **Designer: drag a part onto the board** to place it where it is dropped, and **multi-select**
  with align and distribute.
  *Seret part ke board; pilih banyak widget sekaligus, rata kiri/tengah, sebar merata.*
- **Localised text.** LVGL 9.6 has translation tags (`lv_tabview_set_tab_translation_tag` and
  friends); expose them and let a layout carry translatable strings.
  *Teks multi-bahasa lewat translation tag LVGL 9.6.*
- **Encoder and keypad navigation** for panels with a rotary knob instead of touch.
  *Navigasi encoder dan keypad untuk panel tanpa layar sentuh.*
- **Theme switching** between LVGL's light and dark default themes at run time.
  *Ganti tema terang/gelap saat runtime.*
- **Ship the designer** as self-contained downloads per OS from the release workflow.
  *Rilis designer sebagai unduhan per OS.*

### Later / Nanti
- Pi Zero 2 W / 32-bit ARM in CI (built today, not tested on device).
- `LVGL.Net.Assistant` on stable connectors once Semantic Kernel's Google and Ollama connectors
  leave preview (NU5104 until then).

## Out of scope / Di luar cakupan
- Display or input drivers inside the C build (principle 1).
- Multi-threaded access to LVGL. It is single-threaded by design; `Post`/`Invoke`/`LvTimer` are
  the answer.
- A colour format other than XRGB8888 (every backend blits it unchanged).
- Event handlers or animations inside layout documents: generated code never contains application
  logic; that lives in the hand-written partial half.
