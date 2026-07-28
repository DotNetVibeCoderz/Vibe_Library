using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>A draggable slider.</summary>
public sealed class LvSlider : LvObject
{
    /// <summary>Creates a slider on <paramref name="parent"/>.</summary>
    public LvSlider(LvObject? parent) : base(LvglNative.lv_slider_create(ResolveParent(parent))) { }

    /// <summary>Current value. Assigning animates by default; use <see cref="SetValue"/> to control that.</summary>
    public int Value
    {
        get => LvglNative.lv_slider_get_value(Handle);
        set => SetValue(value, animate: true);
    }

    /// <summary>Sets the value, optionally without animation.</summary>
    public void SetValue(int value, bool animate) => LvglNative.lv_slider_set_value(Handle, value, animate ? 1 : 0);

    /// <summary>Sets the accepted range. Defaults to 0..100.</summary>
    public void SetRange(int minimum, int maximum)
    {
        if (minimum > maximum) throw new ArgumentException("minimum must not exceed maximum.", nameof(minimum));
        LvglNative.lv_slider_set_range(Handle, minimum, maximum);
    }
}
