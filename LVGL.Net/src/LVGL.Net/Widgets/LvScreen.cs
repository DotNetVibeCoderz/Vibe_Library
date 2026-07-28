using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>
/// A root object. Exactly one screen is displayed at a time; <see cref="Load"/> switches between
/// them, which is the usual way to build multi-page applications.
/// </summary>
public sealed class LvScreen : LvObject
{
    private LvScreen(nint handle) : base(handle) { }

    /// <summary>Wraps the screen LVGL is currently showing.</summary>
    public static LvScreen Active()
    {
        var handle = LvglNative.lv_screen_active();
        if (handle == 0) throw new LvglException("There is no active screen; create a display first.");

        // A screen created by LVGL itself (the default one) has no wrapper yet; reuse the existing
        // wrapper when there is one so event subscriptions are not registered twice.
        return FromHandle(handle) as LvScreen ?? new LvScreen(handle);
    }

    /// <summary>Creates a new, empty screen that is not displayed until <see cref="Load"/> is called.</summary>
    public static LvScreen Create()
    {
        LvglRuntime.EnsureUiThread();

        // Passing NULL as the parent makes lv_obj_create build a screen on the default display.
        var handle = LvglNative.lv_obj_create(0);
        return new LvScreen(handle);
    }

    /// <summary>Makes this screen the displayed one.</summary>
    public void Load() => LvglNative.lv_screen_load(Handle);
}
