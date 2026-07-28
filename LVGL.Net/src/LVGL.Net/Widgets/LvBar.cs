using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>A read-only progress bar.</summary>
public sealed class LvBar : LvObject
{
    /// <summary>Creates a bar on <paramref name="parent"/>.</summary>
    public LvBar(LvObject? parent) : base(LvglNative.lv_bar_create(ResolveParent(parent))) { }

    /// <summary>Current value. Assigning animates the indicator.</summary>
    public int Value
    {
        get => LvglNative.lv_bar_get_value(Handle);
        set => SetValue(value, animate: true);
    }

    /// <summary>Sets the value, optionally without animation.</summary>
    public void SetValue(int value, bool animate) => LvglNative.lv_bar_set_value(Handle, value, animate ? 1 : 0);

    /// <summary>Sets the displayed range. Defaults to 0..100.</summary>
    public void SetRange(int minimum, int maximum)
    {
        if (minimum > maximum) throw new ArgumentException("minimum must not exceed maximum.", nameof(minimum));
        LvglNative.lv_bar_set_range(Handle, minimum, maximum);
    }
}
