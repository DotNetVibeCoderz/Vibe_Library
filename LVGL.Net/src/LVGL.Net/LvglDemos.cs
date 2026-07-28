using Lvgl.Interop;

namespace Lvgl;

/// <summary>
/// The demo scenes bundled with LVGL, available when the native library was built with
/// <c>LVGLNET_WITH_DEMOS=ON</c> (the default).
/// </summary>
/// <remarks>
/// These build their widget tree entirely in C, so the objects they create have no managed
/// wrappers. They are useful as a rendering smoke test and as a performance baseline for a new
/// board - not as a starting point for application code.
/// </remarks>
public static class LvglDemos
{
    /// <summary>
    /// Builds LVGL's widget showcase on the active screen.
    /// </summary>
    /// <returns><see langword="false"/> when the native library was built without the demos.</returns>
    public static bool ShowWidgets()
    {
        LvglRuntime.EnsureUiThread();
        return LvglNative.lvn_demo_widgets() != 0;
    }

    /// <summary>
    /// Runs LVGL's rendering benchmark, which reports its results on screen.
    /// </summary>
    /// <returns><see langword="false"/> when the native library was built without the demos.</returns>
    public static bool ShowBenchmark()
    {
        LvglRuntime.EnsureUiThread();
        return LvglNative.lvn_demo_benchmark() != 0;
    }
}
