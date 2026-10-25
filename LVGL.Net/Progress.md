# LVGL.Net — Progress / Kemajuan

Di port oleh Kang Fadhil dari Gravicode Studios. Direction lives in [PLAN.md](PLAN.md); this file
tracks what is done and **how each item was verified**. A box is ticked only when something
automated proves it; `[~]` means built but proven only partly or by hand. The closing section lists
what the ticks do *not* prove.

Arah pengembangan ada di [PLAN.md](PLAN.md); berkas ini mencatat apa yang sudah selesai dan
**bagaimana setiap butir diverifikasi**. Kotak hanya dicentang jika ada hal otomatis yang
membuktikannya; `[~]` berarti sudah dibangun tetapi baru terbukti sebagian atau manual. Bagian
penutup berisi hal-hal yang *tidak* dibuktikan oleh centang-centang itu.

Last verified / Terakhir diverifikasi: 2026-10-26, Windows x64, LVGL 9.6.0, with
`LVGLNET_REQUIRE_NATIVE=1`: `tests/LVGL.Net.Tests` 399 passed, `tests/LVGL.Designer.Tests` 224
passed, 0 failed; Release build of the whole solution with 0 warnings; `templates/test-templates.sh`
passed; the gallery rendered all 17 demos off-screen and ran in an SDL window. The live-model tests passed
against Claude Sonnet 5.5 (`claude-sonnet-5-5`) on the same day. CI run 40240742761 then built and
ran both suites green on **Windows x64, Ubuntu 24.04 x64 and macOS 26 Arm64** (LVGL built natively
on each).

## Requirements / Kebutuhan (requirements.txt)

- [x] **R1**: multi-platform .NET 10 wrapper for LVGL. Core builds for `net10.0` with no
  dependencies; native `lvglnet` is built for six RIDs by `lvgl-net-publish.yml`, which fails the
  release if any is missing
- [x] **R2**: the designer. Now Avalonia and cross-platform. Its editing model is unit-tested
  (`DesignerSessionTests`, `AlignMathTests`) and the real window is driven with pointer and keyboard
  on Avalonia's headless platform (`DesignerWindowTests`): select, drag, resize, undo, inspector,
  code mode, assistant panel, presets, and a pixel check that LVGL's colours reach the canvas
- [~] **R3a**: `LVGL.Raspi` reads Pi sensors and charts them live. `--simulate`, `--screenshot` and
  `--bench` run anywhere; `LinuxSensorSource` on a real Pi 4 is not run automatically yet (see 0.4)
- [~] **R3b**: `LVGL.Desktop` demo browser on Windows/macOS/Linux. CI builds it on all three; every
  page renders off-screen through `--screenshots`; the SDL window itself is not checked
  automatically
- [x] **R3c**: documentation in English and Indonesian - 14 chapters each, mirrored file for file -
  plus a generated API reference that `ApiReferenceTests` keeps in step with the code
- [x] **R4**: light and fast. `The_render_path_does_not_allocate` requires 0 managed bytes over 150
  redrawn frames (chart, value changes, animation, timer); BenchmarkDotNet shows 0 B for every
  render-path call (`docs/benchmarks/render-path.md`)

## Core wrapper / Wrapper inti

- [x] Start-up checks: native load, shim ABI 4, colour depth 32. `NativeLibraryTests`, `LvglIntegrationTests`
- [x] Every managed enum mirror equals the loaded LVGL's values. `LvglConstantsTests` (19 enums)
- [x] `LvCoord` matches LVGL v9 encoding. `LvCoordTests`, cross-checked against `lv_pct`
- [x] Colours and areas through the shim; colours round-trip to pixels exactly. `LvColorTests`, `LvAreaTests`, `LvglIntegrationTests`
- [x] Widgets: Label, Button, Slider, Bar, Arc, Switch, Checkbox, Dropdown, Roller, TextArea, Chart, Panel, Screen. `LvglIntegrationTests`
- [x] Focus groups and layout read-back. `LvglFocusAndLayoutTests`
- [x] Deleted widgets throw instead of passing a null pointer; `GetBounds` and `ZIndex`. `LvglIntegrationTests`, `LvglWidgetCoverageTests`
- [x] Callback exceptions are contained and reported through `CallbackFailed` (events, timers). `LvglWidgetCoverageTests`

## 0.2 — widget coverage / cakupan widget

- [x] `LvImage`: managed pixels, translucency, PNG and BMP files, symbols, transforms. `LvglWidgetCoverageTests` (pixel checks)
- [x] `LvAnimation`: reaches its end value and completes; stops when its target is deleted; stopping one leaves another on the same widget running; helpers. `LvglWidgetCoverageTests`
- [x] `LvTimer`: repeat count, pause, restart, dispose. `LvglWidgetCoverageTests`
- [x] `LvTabView`, `LvList` (click delivered), `LvMessageBox` (modal close), `LvTileView`, `LvKeyboard`. `LvglWidgetCoverageTests`
- [x] `LvSpinner`, `LvLed`, `LvScale`, `LvTable`, `LvSpinbox`. `LvglWidgetCoverageTests`
- [x] TrueType fonts from file and memory, resizing. `LvglWidgetCoverageTests` (uses the font in the LVGL checkout)
- [x] `HeadlessBackend.SavePng`: a screenshot read back by LVGL's own PNG decoder keeps its colour
- [x] The whole stack knows every layout widget: model, validation, builder, generator, designer, assistant. `UiDocumentStructureTests`, `UiBuilderIntegrationTests`, `GeneratedCodeCompilesTests` (Roslyn, every kind), `DesignerSessionTests.Every_part_can_be_added_and_keeps_the_document_valid`

## 0.3 — designer / designer

- [x] Ported to Avalonia; builds on Windows, macOS and Linux in CI
- [x] Direct manipulation: select, drag on the 8 px grid, resize pads that hold the far edge, one undo step per drag. `DesignerWindowTests`; resize write-back for all 22 alignments × 8 edges against LVGL's rules in `AlignMathTests`
- [x] Nesting in every container; tab/tile structure enforced; re-parenting rules. `DesignerSessionTests`, `UiDocumentStructureTests`
- [~] Drag-to-reparent in the outline: the rules are tested; the pointer gesture itself is not automated
- [x] Shared styles: create, apply, rename, delete; reach the screen in LVGL. `DesignerSessionTests`, `UiBuilderIntegrationTests`, `DesignerWindowTests`
- [x] Image and font assets: imported next to the layout, exported next to the generated code, missing files reported. `DesignerSessionTests`, `UiBuilderIntegrationTests`
- [x] Screen-size presets. `DesignerWindowTests`
- [x] Code mode applies edited JSON as an undoable step. `DesignerWindowTests`
- [x] Documentation screenshots are generated from the current code. `DocumentationScreenshots`, `--screenshots`, `--screenshot`

## 0.4 — platform and hardware / platform dan perangkat keras

- [x] LVGL 9.6.0: every imported prototype compared with 9.2.2; renamed and retyped imports fixed; all tests pass against it
- [x] TTF fonts at run time (above)
- [~] `DrmBackend`: ioctl numbers and struct sizes equal the kernel's (`DrmBackendTests`); mode choice and damage tracking tested. **Not yet run on a display** - no Linux machine with KMS was available
- [x] macOS in the CI test matrix: both suites and the bench pass on macOS Arm64 (CI run 40240742761). The first run caught a timing assumption in one designer test, since fixed
- [~] Hardware-in-the-loop: `lvgl-net-pi4.yml` and `--bench` exist; `--bench` verified off-screen (`docs/benchmarks/dashboard-headless-windows-x64.json`). **No Pi run yet** - needs a self-hosted runner
- [x] `LVGLNET_REQUIRE_NATIVE=1` makes a missing or broken native library fail the run. Set in CI

## Templates and gallery / Template dan galeri

- [x] `dotnet new lvgl` (`--layout code|json`) and `dotnet new lvgl-screen`, packed as
  `LVGL.Net.Templates`. `templates/test-templates.sh` packs everything into a local feed, creates
  both variants plus a screen, builds them with warnings as errors and renders each to a PNG -
  passed on Windows; added to CI for all three OSes. `TemplateTests` pins the template's package
  version to `Directory.Build.props` and validates both template layouts
- [x] The publish workflow packs the templates and rewrites their package version to the release's
- [x] `LVGL.Gallery`: 17 demos in six use cases, each with its own source in a Source view.
  `GalleryIntegrationTests` mounts every demo against real LVGL, runs its timers, tears it down and
  builds its Source view with no callback failures; `GalleryEngineTests` covers the calculator,
  unit tables, 2048 rules, minimax (exhaustively: the computer never loses), the highlighter and
  the SQLite store; CI runs `--screenshots`, which fails on any demo error
- [x] Core additions the gallery needed: `LvCanvas`, `LvEventArgs.Key` / `LvKey`,
  `GestureDirection`, `TryGetPointer`, `LvLabel.Recolor`, `ScrollTo`/`ScrollBy`/`ScrollToBottom`/
  `ScrollIntoView`, `LvArc.Mode`. `LvglWidgetCoverageTests` (pixel checks for the canvas, a real
  key sequence, a dragged swipe, a click position)
- [~] The gallery on a Pi touch panel: `--drm`/`--fb` are wired like the Pi sample's, but it has
  not run on one

## Later items / Butir "Nanti"

- [x] Allocation benchmarks: `benchmarks/LVGL.Net.Benchmarks`, results in `docs/benchmarks/render-path.md`
- [x] Generated API reference: `tools/LVGL.ApiDocs` → `docs/api`, freshness enforced by `ApiReferenceTests`
- [x] Cross-platform designer (0.3)

## Assistant / Asisten

- [x] Design plugin validates layouts against the real model; knows the 0.2 widgets and shared styles. `LvglDesignPluginTests`
- [x] The handler half it writes compiles with the generated half - it did not before (missing `using Lvgl.Events`). `GeneratedCodeCompilesTests`
- [x] Anthropic: temperature dropped for models that reject it; workspace id sent for keys that need one. `AnthropicModelTraitsTests`, `AssistantOptionsTests`
- [x] Provider errors show the provider's explanation, not just the status code
- [x] Live end to end with a real model (opt-in): library `LiveAssistantTests` and designer `LiveAssistantWindowTests` passed against `claude-sonnet-5-5` - the model called `create_layout`, the layout validated and landed on the canvas as an undoable step
- [ ] Live tests for OpenAI, Gemini and Ollama: the tests support them (`LVGLNET_LIVE_PROVIDER`) but have not been run against them

## Packaging / Paket

- [x] Five packages with README, licence and icon, plus `LVGL.Net.Templates`. Checked by `dotnet pack`
- [x] Native libraries for six RIDs inside `LVGL.Net`; the publish workflow fails if any is absent
- [ ] `LVGL.Net.Assistant` depends on prerelease SK connectors (Google, Ollama) — NU5104 on pack. No stable versions exist yet

## Measured / Terukur

| What | Value | Where |
|---|---|---|
| Full 800 × 480 redraw | 0.54 ms | i7-8650U, `docs/benchmarks/render-path.md` |
| Chart sample / bar value / colour change | 129 / 133 / 272 ns, 0 B | same |
| Dashboard, continuous redraw, off-screen | 63.6 fps, p99 2.1 ms, 0 GC, 34.7 MiB working set | `docs/benchmarks/dashboard-headless-windows-x64.json` |
| Pi dashboard draw buffers | 200 KiB at 800 × 480 | `--bench` report (the old docs said ~100 KiB; corrected) |
| Dashboard bench in CI, off-screen | Ubuntu x64 228.9 fps, p99 0.42 ms; macOS Arm64 56.9 fps, p99 1.30 ms; 0 GC on both | CI artifacts `bench-*.json`, run 40240742761 |

## What the ticks do not prove / Yang tidak dibuktikan oleh centang

- **Real hardware.** Nothing has run on a Raspberry Pi or on a KMS display yet; the Pi workflow
  is ready but needs a runner. *Belum ada yang berjalan di Pi atau layar KMS sungguhan.*
- **Desktop Linux and macOS by hand.** CI runs every test on both, but nobody has used the
  designer interactively there yet. *Designer belum dicoba manual di Linux/macOS.*
- **The SDL window.** Rendering is verified off-screen; SDL's window and texture upload are checked
  by eye. *Jendela SDL diperiksa secara manual.*
- **Designer gestures not automated:** drag-to-reparent in the outline, the colour picker flyout,
  file pickers. *Beberapa gestur designer belum diotomasi.*
- **AI output quality.** The live tests prove the plumbing and that a valid layout comes back,
  not that it is a good design. *Kualitas desain dari model tidak diuji.*
