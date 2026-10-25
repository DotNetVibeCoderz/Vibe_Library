# Testing and benchmarks

## The test suites

| Project | Framework | What it covers |
|---|---|---|
| `tests/LVGL.Net.Tests` | xUnit | The library: unit tests, integration tests against a real LVGL build, the layout model, the code generator, the assistant's plugins, the DRM backend's kernel ABI, the API reference |
| `tests/LVGL.Designer.Tests` | xUnit v3 + Avalonia.Headless | The designer: its editing model, the resize geometry, and the real window driven by pointer and keyboard |

```bash
dotnet test LVGL.Net.slnx
```

### Without and with the native library

The unit tests need nothing; integration tests check for LVGL and return early when it is missing,
so the suite runs on a machine without CMake. Once you have built the native library, run with

```bash
LVGLNET_REQUIRE_NATIVE=1 dotnet test LVGL.Net.slnx
```

so that a library which fails to load fails the run, instead of every integration test quietly
passing as a skip. CI always runs this way.

### What the integration tests prove

- **Rendering.** Colours set through the ABI shim come back exactly in the rendered pixels; images
  from managed pixels, PNG files and BMP files render their colours; a PNG written by
  `HeadlessBackend.SavePng` is read back by LVGL's own decoder and still has the right colour.
- **The ABI.** Every managed enum that mirrors an LVGL enum is compared, member by member, with the
  values in the loaded build (`LvglConstantsTests`); every event code resolves; the coordinate
  encoding agrees with LVGL's own `lv_pct`.
- **Lifetime.** Deleting a widget kills its wrapper and its children's; deleting an animation's
  target stops the animation; styles, fonts and images are freed after the widgets.
- **The render path does not allocate.** 150 redrawn frames - with a chart streaming, a bar
  changing, an animation and a timer running - must allocate exactly 0 bytes on the managed heap.
- **Layouts.** A "kitchen sink" document with every widget kind, shared styles, a font and an image
  asset builds into live widgets; shared styles reach the screen; a missing asset is a diagnostic,
  not a crash.

### The generated code compiles

The code generator's tests compile its output with Roslyn against the real `LVGL.Net` assembly - for
every widget kind on its own, for the kitchen-sink layout, and together with the event-handler half
the assistant writes. A generator change that emits code which does not build fails here.

### The designer, driven like a user

The designer tests open the real window on Avalonia's headless platform, with LVGL rendering the
preview, and use pointer and keyboard input: click a widget on the board and check the selection;
drag it and check it moved onto the grid and undoes in one step; pull a copper pad and check the
far edge held still; type a name in the inspector and check undo reverts the whole word; apply
edited JSON in code mode; open the assistant panel. One test reads the pixel at a button's centre
and checks it is the colour LVGL drew.

The resize geometry is also tested on its own, against an independent transcription of LVGL's
alignment rules, for every alignment and every edge (176 cases).

### Against a real model

Opt-in, because it needs an API key and costs a request - see
[the assistant chapter](09-assistant.md#testing-against-a-real-model).

## Screenshots

Every screenshot in these documents is generated, so they can be refreshed after a change rather
than retaken by hand:

```bash
dotnet run --project samples/LVGL.Desktop -c Release -- --screenshots docs/images/screenshots
dotnet run --project samples/LVGL.Raspi -c Release -- --screenshot docs/images/screenshots/raspi-dashboard.png
LVGLNET_DOC_SCREENSHOTS=$PWD/docs/images/screenshots dotnet test tests/LVGL.Designer.Tests --filter DocumentationScreenshots
```

The designer images use `samples/layouts/smart-home.lvgl.json`. The assistant screenshot comes
from the live test, with `LVGLNET_DOC_SCREENSHOTS` set as well.

## Benchmarks

`benchmarks/LVGL.Net.Benchmarks` measures the calls a live dashboard makes every frame with
BenchmarkDotNet, time and managed allocation:

```bash
dotnet run -c Release --project benchmarks/LVGL.Net.Benchmarks -- --filter '*' --job short
```

Measured on an Intel Core i7-8650U, .NET 10.0.12, LVGL 9.6.0, 800 × 480
(`docs/benchmarks/render-path.md`):

| Call | Mean | Allocated |
|---|---:|---:|
| `ChartAddPoint` | 129 ns | 0 B |
| `SetBarValue` | 133 ns | 0 B |
| `SetBackgroundColor` (through the colour shim) | 272 ns | 0 B |
| `RunFrameIdle` (nothing to redraw) | 71 ns | 0 B |
| `RedrawFullScreen` (invalidate and render 800 × 480) | 544 µs | 0 B |
| `SetLabelText` | 300 ns | 0 B |

`SetLabelText` allocates nothing as well, because `[LibraryImport]` marshals a short string to
UTF-8 on the stack.

The Pi sample's `--bench` mode measures the whole dashboard end to end; see
[the Raspberry Pi guide](07-raspberry-pi.md#measuring---bench).

## The API reference

`docs/api` is generated from the XML documentation by `tools/LVGL.ApiDocs`:

```bash
dotnet run --project tools/LVGL.ApiDocs
```

A test regenerates it into a temporary folder and fails if the committed copy differs, so a change
to a public API without regenerating the reference does not get through CI.
