# Lvgl.Interop

[API reference](README.md) · generated from the XML documentation; do not edit by hand.

## LvglNativeLibrary

`static class` in `LVGL.Net`

Locates the `lvglnet` shared library.

The default probing logic only finds the library when it sits next to the entry assembly or
arrives through a NuGet `runtimes/` folder. During development it is produced by
`native/build.sh` into the repository's `runtimes/<rid>/native`, which may be
several directories above the output folder, so an explicit resolver is installed.

| Member | Description |
|--------|-------------|
| `IReadOnlyList<string> ProbedPaths { get; }` | Paths that were probed during the last failed load, for diagnostics. |
| `static IEnumerable<string> EnumerateProbePaths(string fileName)` | The directories LVGL.Net searches for a native dependency, in order: the application directory and each of its ancestors, both directly and under `runtimes/<rid>/native/`.<br>`fileName`: Platform-specific file name, e.g. `SDL2.dll` or `libSDL2.so`. |
| `static string NativeFileName(string libraryName)` | Maps a bare library name onto the platform's file naming convention. |
| `static void Preload()` | Loads the native library eagerly so a missing or mismatched build fails with a clear message instead of a `DllNotFoundException` from an arbitrary call site. |
| `static bool TryLoadFromProbePaths(string fileName, out nint handle)` | Tries to load a native library by file name from the LVGL.Net probe paths, then from the OS loader's own search path. |
| `const string PathVariable` | Environment variable holding an explicit path to the shared library or to the directory containing it. Checked before every other probe path. |

## LvglNativeNotFoundException

`sealed class` in `LVGL.Net`

Thrown when the `lvglnet` shared library cannot be found or loaded.

| Member | Description |
|--------|-------------|
| `LvglNativeNotFoundException(string message)` | Thrown when the `lvglnet` shared library cannot be found or loaded. |

