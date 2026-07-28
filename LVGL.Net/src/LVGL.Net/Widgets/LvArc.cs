using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>
/// A circular arc, used both as a dial-style indicator and as a rotary input.
/// </summary>
public sealed class LvArc : LvObject
{
    /// <summary>Creates an arc on <paramref name="parent"/>.</summary>
    public LvArc(LvObject? parent) : base(LvglNative.lv_arc_create(ResolveParent(parent))) { }

    /// <summary>Current value.</summary>
    public int Value
    {
        get => LvglNative.lv_arc_get_value(Handle);
        set => LvglNative.lv_arc_set_value(Handle, value);
    }

    /// <summary>Sets the accepted range. Defaults to 0..100.</summary>
    public void SetRange(int minimum, int maximum)
    {
        if (minimum > maximum) throw new ArgumentException("minimum must not exceed maximum.", nameof(minimum));
        LvglNative.lv_arc_set_range(Handle, minimum, maximum);
    }

    /// <summary>
    /// Sets the angular extent of the background track, in degrees clockwise from the 3 o'clock
    /// position. A gauge typically uses 135..45 together with <see cref="Rotation"/>.
    /// </summary>
    public void SetBackgroundAngles(int startAngle, int endAngle) =>
        LvglNative.lv_arc_set_bg_angles(Handle, startAngle, endAngle);

    /// <summary>Rotates the whole arc, in degrees.</summary>
    public int Rotation
    {
        set => LvglNative.lv_arc_set_rotation(Handle, value);
    }

    /// <summary>Makes the arc display-only by removing its click handling.</summary>
    public bool IsReadOnly
    {
        get => !HasFlag(LvObjFlag.Clickable);
        set => SetFlag(LvObjFlag.Clickable, !value);
    }
}
