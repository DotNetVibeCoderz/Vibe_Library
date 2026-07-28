# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

LVGL.Net wraps [LVGL](https://github.com/lvgl/lvgl) v9 (a C embedded-graphics library) for .NET 10,
with a WPF designer and two sample applications. The original specification is `requirements.txt`
(Bahasa Indonesia).

**Attribution: "Di port oleh Kang Fadhil dari Gravicode Studios."** It lives in
`Directory.Build.props` (assembly metadata) and is surfaced through `LvglAbout` — use that class
rather than hard-coding the string in new UI or console banners.

## Commands

```bash
# Native library - required once before anything renders
./native/build.sh                   # Linux/macOS; --pi4 tunes for Cortex-A72, --no-demos skips demos
./native/build.ps1                  # Windows; finds VS's bundled CMake/Ninja/MSVC itself, -Clean to rebuild

# Managed
dotnet build LVGL.Net.slnx          # note: .slnx, not .sln
dotnet test  LVGL.Net.slnx
dotnet test  tests/LVGL.Net.Tests --filter "FullyQualifiedName~LvCoordTests"

# Run
dotnet run --project samples/LVGL.Desktop            # SDL demo browser
dotnet run --project samples/LVGL.Raspi -- --sdl --simulate
dotnet run --project tools/LVGL.Designer             # Windows only
```

The unit tests run **without** the native library, so `dotnet test` works on a machine without
CMake. `LvglIntegrationTests` exercises a real LVGL build and no-ops via `LvglFixture.IsAvailable`
when it is absent. Any new test must degrade the same way.

LVGL is process-global and single-threaded, so integration tests share one `LvglFixture` that owns
LVGL on a dedicated thread; test bodies are marshalled onto it with `lvgl.Invoke(...)`. The
collection has `DisableParallelization = true` — do not add a second fixture.

## Architecture

Layers: application → `LVGL.Net` (widgets, run loop) → P/Invoke → `lvglnet` (LVGL v9 + `lvn_*`
shim), with `ILvglBackend` on the managed side owning all platform code.

**The native library has no display drivers, on purpose.** LVGL renders into a buffer and calls a
flush callback; getting pixels to a screen is managed code behind `ILvglBackend` (SDL, Linux
framebuffer, headless). One native artifact serves every target and it links only libc/libm. When
adding a new output device, write a backend — do not add a driver to the C build.

**`native/shim/lvglnet_shim.c` exists for things P/Invoke cannot do safely.** Read the header
comment before adding any interop. The rules:

- Never declare an import taking or returning `lv_color_t` by value — a 3-byte struct goes in a
  register on SysV/AAPCS but by hidden pointer on Windows x64. Use the `lvn_*` colour wrappers,
  which take packed `0xRRGGBB`.
- `lv_style_t` allocation, `lv_indev_data_t`/`lv_area_t` field access and the Montserrat font
  globals all go through the shim.
- Event codes go through `lvn_event_code(stableId)`: LVGL inserted `LV_EVENT_ROTARY` mid-enum in
  9.1, so `lv_event_code_t` ordinals are not a stable ABI. `LvEventCode` holds LVGL.Net's own
  stable ids.
- Some LVGL helpers are `static inline` in the headers and therefore export **no symbol** —
  `lv_obj_set_style_pad_all` and `lv_style_set_pad_all` are the ones hit so far. They are
  re-exported as `lvn_*`. If a new import fails with `EntryPointNotFoundException`, check the
  header before assuming the name is wrong: `dumpbin /EXPORTS` on the built DLL lists the truth.
- Bump `LVN_ABI_VERSION` and `LvglRuntime.ExpectedShimAbi` together when an `lvn_*` signature
  changes or a new required entry point is added.

**`LV_COLOR_DEPTH` is fixed at 32** and validated at startup. Backends blit raw XRGB8888; changing
the depth without changing every backend produces garbled output.

**Threading.** LVGL is single-threaded and unsynchronised. `LvglRuntime` records the owning thread
and `EnsureUiThread()` enforces it. Background work uses `LvglApplication.Post` / `Invoke`.

**Callbacks never let a managed exception unwind into C.** The display flush parks the exception for
the run loop to rethrow; event handlers swallow theirs. Display flush resolves its instance from a
`GCHandle` in the display user data (hot path, no lookup); widget events resolve through a static
registry keyed by native pointer — deliberately *not* a `GCHandle`, because LVGL fires callbacks
while deleting an object and reading a freed handle is undefined behaviour.

**Widget lifetime.** LVGL owns the tree, so `LvObject` is not `IDisposable`; use `Delete()` or
`Clear()`. `LvStyle` *is* disposable and must outlive every widget referencing it — dispose styles
only after the widgets are gone.

## Traps specific to this codebase

- **Never do arithmetic on `LvCoord.Percent(...)` or `LvCoord.SizeContent`.** LVGL encodes the flag
  in the high bits, so `Percent(100) - 190` silently becomes a meaningless pixel count. Compute in
  pixels from `app.Display.Width/Height`. A regression test in `LvCoordTests` documents this.
- `LvCoord` mirrors `lv_area.h` **v9**, which differs from v8: `LV_SIZE_CONTENT` is `LV_COORD_MAX`
  (not 2001) and negative percentages are stored against `LV_PCT_POS_MAX` (not 1000). Getting this
  wrong misplaces widgets silently; `LvglIntegrationTests` cross-checks every value against LVGL's
  own `lv_pct`.
- `LvObject.Handle` **throws** once the widget is deleted rather than returning zero — LVGL
  dereferences a null `lv_obj_t*` without checking, so returning zero turned a stale reference into
  an access violation. Check `IsAlive` for references that may outlive their widget.
- Reading back a computed `Width`/`Height` needs `UpdateLayout()` first; LVGL otherwise resolves
  layout during the next refresh and reports 0 for a widget that has never been laid out.
- **Ordering when tearing down a page:** delete widgets first, then dispose shared styles.
- **`LvSymbols` builds its glyphs from numeric code points** via `char.ConvertFromUtf32`, not from
  literals — keep it that way so the file stays ASCII and cannot be corrupted by re-encoding.
- The solution file is `LVGL.Net.slnx` (the .NET 10 default), not `.sln`.
- `Directory.Build.props` sets `EnableDefaultNoneItems=false`, which stops the SDK globbing
  `App.config` — so the designer includes it explicitly. Without that entry the app runs but
  `ConfigurationManager` sees no config and every provider reports "no API key".
- **WPF cannot run under `InvariantGlobalization`** (also set solution-wide); the designer
  overrides it to false. With it on, the first templated `ListBox` item throws
  `Cannot find non-neutral culture` and the app dies at startup.
- **Claude removed `temperature` on its current models** (Opus 5/4.8/4.7, Sonnet 5, Fable 5) — it
  is a 400, not an ignored field. `AnthropicModelTraits` decides per model and the connector drops
  the value; `AnthropicModelTraitsTests` pins the family list. Do not "simplify" that away.
- Semantic Kernel has no Anthropic connector, so `AnthropicChatCompletionService` implements
  `IChatCompletionService` over the official SDK and runs **its own tool loop**. SK's automatic
  function invocation must stay off for Anthropic or every tool fires twice.
- Attachment URLs are loopback, which a hosted model cannot fetch. Images are therefore also sent
  as inline bytes. Keep both paths when touching `AssistantService.BuildUserMessage`.

## Conventions

- Core library takes no NuGet dependencies and no UI framework. WPF belongs only in
  `tools/LVGL.Designer`; SDL only in `LVGL.Net.Sdl`; Linux syscalls only in `LVGL.Net.Linux`.
  Assistant/AI code belongs only in `LVGL.Net.Assistant`, which holds no WPF — the designer's chat
  window is presentation over `AssistantService`.
- Optimise for low allocation on the render and interop paths — this targets a Raspberry Pi.
  `[LibraryImport]` over `DllImport`, unmanaged function pointers over delegates, unmanaged draw
  buffers, no per-sample allocation in chart updates.
- `UiNode`'s optional properties are nullable to distinguish "LVGL default" from "explicitly zero".
  Preserve that when adding properties, and add them to `UiBuilder`, `CSharpUiGenerator` and the
  designer's `NodeViewModel` together.
- **Documentation changes go in both `docs/en/` and `docs/id/`.** The file sets mirror each other;
  the Indonesian filenames are translated (`04-widget.md` ↔ `04-widgets.md`).
