# Building the native library

## What gets built

One shared library, `lvglnet`, containing upstream LVGL v9.6 plus the ABI shim from `native/shim`.
One artifact means one `DllImport` name and no load-order problems between co-dependent libraries.

| Platform | File | Staged to |
|----------|------|-----------|
| Windows | `lvglnet.dll` | `runtimes/win-x64/native/` |
| Linux | `liblvglnet.so` | `runtimes/linux-x64/native/` or `linux-arm64` |
| macOS | `liblvglnet.dylib` | `runtimes/osx-arm64/native/` or `osx-x64` |

## Quick build

```bash
./native/build.sh                # host build
./native/build.sh --pi4          # tuned for Cortex-A72 (Raspberry Pi 4)
./native/build.sh --no-demos     # skip LVGL's bundled demo scenes
```

## Cross-compiling

`lvglnet` links nothing but libc and libm, so one x64 Linux machine builds every Linux target
and one Mac builds both Apple targets. This is how the release workflow produces all six RIDs
from three runners.

```bash
sudo apt install gcc-aarch64-linux-gnu gcc-arm-linux-gnueabihf

./native/build.sh --cross=aarch64-linux-gnu      # -> runtimes/linux-arm64/native
./native/build.sh --cross=arm-linux-gnueabihf    # -> runtimes/linux-arm/native
```

`--cross` takes a GNU triplet and derives the rest from it: the compiler is `<triplet>-gcc` and
the triplet's first component becomes `CMAKE_SYSTEM_PROCESSOR`, which is what `CMakeLists.txt`
maps onto the RID.

On macOS the second architecture comes out of the same SDK, but `CMAKE_SYSTEM_PROCESSOR` still
reports the host - so the RID has to be named:

```bash
./native/build.sh --osx-arch=x86_64 --rid=osx-x64
```

Each target gets its own build tree (`native/build-<target>`), because a CMake cache remembers
the compiler it was configured with and refuses to be reused across architectures.

```powershell
./native/build.ps1
./native/build.ps1 -NoDemos
```

## Manual CMake

```bash
cmake -S native -B native/build -DCMAKE_BUILD_TYPE=Release
cmake --build native/build --config Release
```

| Option | Default | Effect |
|--------|---------|--------|
| `LVGL_TAG` | `v9.6.0` | Which LVGL release to fetch |
| `LVGLNET_WITH_DEMOS` | `ON` | Compile `lv_demo_widgets` and `lv_demo_benchmark` |
| `LVGLNET_TUNE_PI4` | `OFF` | Add `-mcpu=cortex-a72`; makes the binary non-portable |
| `LVGLNET_RID` | auto-detected | Which `runtimes/<rid>/native` folder to stage into |

A checkout at `native/lvgl` is used in preference to fetching, so offline and air-gapped builds
work: clone LVGL there yourself and CMake will use it.

## Configuration: `native/lv_conf.h`

Deliberately small. LVGL's `lv_conf_internal.h` supplies a default for every option that is not
defined, so only the settings LVGL.Net actually depends on are listed — which means a new LVGL
point release does not have to be merged by hand.

The one setting you must not change:

```c
#define LV_COLOR_FORMAT_DEFAULT LV_COLOR_FORMAT_XRGB8888
```

The backends blit raw XRGB8888 words. Since LVGL 9.5 the format is the setting and
`LV_COLOR_DEPTH` (32) is derived from it - defining the depth directly is deprecated, with a
`#warning` that MSVC's C11 mode treats as an error. A 16-bit build would produce garbled output, so
`LvglRuntime.Initialize` rejects it with an explicit message rather than drawing nonsense.

Features compiled in that the managed side relies on:

| Setting | Used by |
|---------|---------|
| `LV_USE_LODEPNG`, `LV_USE_BMP` | `LvImage` file sources - PNG and BMP are decoded natively |
| `LV_USE_FS_STDIO`, `LV_FS_STDIO_LETTER 'A'` | Every file path handed to LVGL; `LvFileSystem.ToLvglPath` adds the `A:` |
| `LV_USE_TINY_TTF`, `LV_TINY_TTF_FILE_SUPPORT` | `LvFont.FromFile` / `FromData` |
| `LV_CACHE_DEF_SIZE` (1 MiB) | Decoded images are cached rather than re-inflated on every redraw |
| `LV_USE_LED`, `LV_USE_SPINBOX`, and the other widget flags | The widget classes of the same names |

Settings you might reasonably change:

| Setting | Why |
|---------|-----|
| `LV_FONT_MONTSERRAT_*` | Each enabled size costs binary space; disable the ones you never use |
| `LV_USE_*` widget flags | Turning off unused widgets shrinks the library |
| `LV_DEF_REFR_PERIOD` | Refresh cadence in ms; 16 ≈ 60 FPS |
| `LV_DRAW_SW_DRAW_UNIT_CNT` | Extra draw threads. Left at 1: on a 4-core Pi they mostly add contention |
| `LV_USE_LOG` | Set to 1 while debugging a rendering problem |

After editing, rebuild the native library. Managed code does not need rebuilding.

## Moving to a newer LVGL

LVGL keeps its function names stable across a major version but not its enum ordinals, and it
sometimes turns a function into a macro (in 9.6, `lv_chart_set_range` became a macro for
`lv_chart_set_axis_range`, so the old name is no longer exported). The upgrade from 9.2.2 to 9.6.0
went like this, and the same steps apply next time:

1. Change `LVGL_TAG`, delete `native/lvgl` (or move it aside to diff against), rebuild.
2. Compare the prototypes of every imported function between the two header trees - argument
   types change silently: `lv_state_t` grew from 16 to 32 bits, `lv_anim_enable_t` became a `bool`.
3. Run the tests with `LVGLNET_REQUIRE_NATIVE=1`. The constants test compares every managed enum
   mirror against the new build through `lvn_constant`; the event-code test does the same for
   events.
4. Bump `LVN_ABI_VERSION` and `LvglRuntime.ExpectedShimAbi` together if the shim changed.

`LvglConstantsTests` is the safety net here: between 9.2 and 9.6 it would have caught `LV_STATE_ALT`
shifting every state bit and `LV_CHART_TYPE_CURVE` moving `Bar` and `Scatter` - neither of which
fails to compile or link.

## Cross-compiling for the Pi

Two options, in order of preference:

**Build on the Pi.** Simplest and reliable:

```bash
sudo apt install cmake build-essential git
./native/build.sh --pi4
```

**Cross-compile from x86-64 Linux:**

```bash
sudo apt install gcc-aarch64-linux-gnu
cmake -S native -B native/build-arm64 \
      -DCMAKE_BUILD_TYPE=Release \
      -DCMAKE_C_COMPILER=aarch64-linux-gnu-gcc \
      -DCMAKE_SYSTEM_NAME=Linux \
      -DCMAKE_SYSTEM_PROCESSOR=aarch64 \
      -DLVGLNET_RID=linux-arm64 \
      -DLVGLNET_TUNE_PI4=ON
cmake --build native/build-arm64
```

`LVGLNET_RID` must be set explicitly when cross-compiling: without it the staging path would be
guessed from the host.

## How the managed side finds the library

`LvglNativeLibrary` installs a resolver that probes, in order:

1. `LVGLNET_NATIVE_PATH` — a full path to the file, or a directory containing it
2. `AppContext.BaseDirectory` and each of its ancestors, both directly and under
   `runtimes/<rid>/native/`
3. The OS loader's own search path

Walking up from the output directory is what makes `dotnet run --project samples/...` work against
a repository-level `runtimes/` folder without any copy step.

When every probe fails, the exception lists the paths it tried and the command that builds the
library.

## Verifying a build

```csharp
LvglRuntime.Initialize();
Console.WriteLine(LvglRuntime.LvglVersion);   // e.g. 9.6.0
Console.WriteLine(LvglRuntime.ColorDepth);    // must be 32
```

Or just run the desktop sample and open the *LVGL demos* page — if the widget showcase renders, the
native build is complete.
