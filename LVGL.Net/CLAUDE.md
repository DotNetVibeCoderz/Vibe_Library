# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

LVGL.Net wraps [LVGL](https://github.com/lvgl/lvgl) v9.6 (a C embedded-graphics library) for .NET 10,
with an Avalonia designer and two sample applications. The original specification is
`requirements.txt` (Bahasa Indonesia). Direction lives in `PLAN.md`, verified status in
`Progress.md` - a box there is ticked only when something automated proves it.

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
LVGLNET_REQUIRE_NATIVE=1 dotnet test LVGL.Net.slnx   # fail, don't skip, if LVGL won't load
dotnet test  tests/LVGL.Net.Tests --filter "FullyQualifiedName~LvCoordTests"

# Run
dotnet run --project samples/LVGL.Desktop            # SDL demo browser
dotnet run --project samples/LVGL.Raspi -- --sdl --simulate
dotnet run --project tools/LVGL.Designer             # Avalonia - Windows, macOS, Linux
dotnet run --project tools/LVGL.ApiDocs              # regenerate docs/api after a public API change
dotnet run -c Release --project benchmarks/LVGL.Net.Benchmarks -- --filter '*' --job short
```

The unit tests run **without** the native library, so `dotnet test` works on a machine without
CMake. Integration tests exercise a real LVGL build and skip via `lvgl.ShouldSkip()` when it is
absent - which throws instead when `LVGLNET_REQUIRE_NATIVE=1` (CI sets it), so a broken native
build cannot pass as a skip. Any new test must degrade the same way, through `ShouldSkip()`.

`tests/LVGL.Designer.Tests` is a separate project (xUnit v3, Avalonia.Headless): the designer owns
LVGL on the Avalonia UI thread and must not share a process with `LvglFixture`. Opt-in extras:
`LVGLNET_LIVE_LLM=1` (+ a provider key) runs the live-model tests; `LVGLNET_DOC_SCREENSHOTS=<dir>`
regenerates the designer screenshots in `docs/images/screenshots`.

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
- **Enum ordinals are not stable either.** Every managed enum mirroring an LVGL enum (`LvState`,
  `LvChartType`, `LvImageAlign`, ...) must have its constants in the shim's `lvn_constants` table;
  `LvglConstantsTests` compares them with the loaded build. 9.5 inserted `LV_STATE_ALT` at bit 0 and
  `LV_CHART_TYPE_CURVE` before `BAR` - neither fails to compile or link.
- Check argument *types*, not just names, when LVGL moves: `lv_state_t` went from 16 to 32 bits and
  `lv_anim_enable_t` became `bool` between 9.2 and 9.6. `docs/en/03-native-build.md` has the
  upgrade procedure.
- `lv_anim_t` and `lv_image_dsc_t` are built inside the shim. Animations and timers are routed to
  managed code by an **id** held in LVGL (`var` / user data), never a `GCHandle`, for the same reason
  as widget events.

**The colour format is fixed at XRGB8888** (`LV_COLOR_FORMAT_DEFAULT` in `lv_conf.h`; LVGL derives
`LV_COLOR_DEPTH` 32 from it since 9.5 - defining the depth directly is deprecated and breaks the
MSVC build). It is validated at startup. Backends blit raw XRGB8888; changing it without changing
every backend produces garbled output.

**Threading.** LVGL is single-threaded and unsynchronised. `LvglRuntime` records the owning thread
and `EnsureUiThread()` enforces it. Background work uses `LvglApplication.Post` / `Invoke`.

**Callbacks never let a managed exception unwind into C.** The display flush parks the exception for
the run loop to rethrow; event handlers, timers and animations contain theirs and report them
through `LvglRuntime.CallbackFailed`. Display flush resolves its instance from a
`GCHandle` in the display user data (hot path, no lookup); widget events resolve through a static
registry keyed by native pointer — deliberately *not* a `GCHandle`, because LVGL fires callbacks
while deleting an object and reading a freed handle is undefined behaviour.

**Widget lifetime.** LVGL owns the tree, so `LvObject` is not `IDisposable`; use `Delete()` or
`Clear()`. `LvStyle`, `LvFont` (TrueType) and `LvImageSource` (pixels) *are* disposable and must
outlive every widget referencing them — dispose them only after the widgets are gone. A `UiBuilder`
owns the styles and fonts of what it built, builds once, and is disposed after its widgets.

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
- **Ordering when tearing down a page:** delete widgets first, then dispose shared styles, fonts,
  image sources and builders.
- File paths handed to LVGL need the `A:` drive prefix (`LvFileSystem.ToLvglPath`); without it LVGL
  reads the `C` of a Windows path as a drive letter.
- `LvTimer` turns LVGL's auto-delete off on purpose - an auto-deleted timer would leave the wrapper
  holding a freed pointer. An exhausted timer pauses instead.
- Since LVGL 9.5 a chart's grid uses the **main** part's line style and every point gets a marker;
  `SetLineColor(color)` and `SetPartSize(0, 0, LvPart.Indicator)`.
- **`LvSymbols` builds its glyphs from numeric code points** via `char.ConvertFromUtf32`, not from
  literals — keep it that way so the file stays ASCII and cannot be corrupted by re-encoding.
- The solution file is `LVGL.Net.slnx` (the .NET 10 default), not `.sln`.
- `Directory.Build.props` sets `EnableDefaultNoneItems=false`, which stops the SDK globbing
  `App.config` — so the designer includes it explicitly. Without that entry the app runs but
  `ConfigurationManager` sees no config and every provider reports "no API key". Keep the key
  values in `App.config` **empty**: a placeholder like `sk-proj-xxx` makes a provider look configured.
- The designer, its tests and the benchmarks set `InvariantGlobalization=false` (it is on
  solution-wide for the embedded targets); text shaping in the UI wants ICU.
- **Avalonia 12 specifics** met in the designer: named `ColumnDefinition`s get no generated field
  (index `Grid.ColumnDefinitions` instead); `TextBox.TextChanged` is raised after the fact, also for
  the initial value, so inspector fields compare against the current value before treating it as an
  edit; clipboard text is `ClipboardExtensions.SetTextAsync`; the headless frame capture is RGBA.
- `DesignerSession` (in `tools/LVGL.Designer/Core`) is the designer's whole editing model and has no
  Avalonia dependency - put rules there, not in views, and test them in `DesignerSessionTests`.
- **Claude removed `temperature` on its current models** (Opus 5/4.8/4.7, Sonnet 5, Fable 5) — it
  is a 400, not an ignored field. `AnthropicModelTraits` decides per model and the connector drops
  the value; `AnthropicModelTraitsTests` pins the family list. Do not "simplify" that away.
- Semantic Kernel has no Anthropic connector, so `AnthropicChatCompletionService` implements
  `IChatCompletionService` over the official SDK and runs **its own tool loop**. SK's automatic
  function invocation must stay off for Anthropic or every tool fires twice.
- Attachment URLs are loopback, which a hosted model cannot fetch. Images are therefore also sent
  as inline bytes. Keep both paths when touching `AssistantService.BuildUserMessage`.
- Anthropic keys not scoped to a workspace are refused without an `anthropic-workspace-id` header;
  the connector sends `Assistant:Anthropic:WorkspaceId` / `ANTHROPIC_WORKSPACE_ID` on its own
  `HttpClient`. Provider errors carry the provider's response body into the transcript - keep that.

## Conventions

- Core library takes no NuGet dependencies and no UI framework. Avalonia belongs only in
  `tools/LVGL.Designer`; SDL only in `LVGL.Net.Sdl`; Linux syscalls (fbdev, DRM/KMS ioctls, evdev)
  only in `LVGL.Net.Linux`, declared directly - no libdrm. Assistant/AI code belongs only in
  `LVGL.Net.Assistant`, which holds no UI — the designer's chat panel is presentation over
  `AssistantService`.
- The render path must stay allocation-free; `The_render_path_does_not_allocate` enforces it.
- Optimise for low allocation on the render and interop paths — this targets a Raspberry Pi.
  `[LibraryImport]` over `DllImport`, unmanaged function pointers over delegates, unmanaged draw
  buffers, no per-sample allocation in chart updates.
- `UiNode`'s optional properties are nullable to distinguish "LVGL default" from "explicitly zero".
  Preserve that when adding properties, and add them to `UiBuilder`, `CSharpUiGenerator`, the
  designer's `InspectorPanel`, `UiDocument.Validate` and the assistant's `DescribeType` together.
  A new widget kind also needs a `Designators` prefix; `GeneratedCodeCompilesTests` compiles every
  kind, so a missing case fails there.
- After changing a public API, regenerate `docs/api` (`ApiReferenceTests` fails until you do).
- **Documentation changes go in both `docs/en/` and `docs/id/`.** The file sets mirror each other;
  the Indonesian filenames are translated (`04-widget.md` ↔ `04-widgets.md`).
