using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>
/// A ruler or gauge face: ticks and labels along a line or an arc. LVGL v9's replacement for the
/// v8 meter.
/// </summary>
/// <remarks>
/// Pair a round scale with an <see cref="LvArc"/> or a needle drawn on top to build a gauge; the
/// scale itself shows no value.
/// </remarks>
public sealed class LvScale : LvObject
{
    /// <summary>Creates a scale on <paramref name="parent"/>.</summary>
    public LvScale(LvObject? parent) : base(LvglNative.lv_scale_create(ResolveParent(parent))) { }

    /// <summary>Shape and tick side.</summary>
    public LvScaleMode Mode
    {
        get => (LvScaleMode)LvglNative.lv_scale_get_mode(Handle);
        set => LvglNative.lv_scale_set_mode(Handle, (int)value);
    }

    /// <summary>Sets the values the first and last tick stand for.</summary>
    public void SetRange(int minimum, int maximum)
    {
        if (minimum > maximum) throw new ArgumentException("minimum must not exceed maximum.", nameof(minimum));
        LvglNative.lv_scale_set_range(Handle, minimum, maximum);
    }

    /// <summary>Value of the first tick.</summary>
    public int Minimum => LvglNative.lv_scale_get_range_min_value(Handle);

    /// <summary>Value of the last tick.</summary>
    public int Maximum => LvglNative.lv_scale_get_range_max_value(Handle);

    /// <summary>Number of ticks, major and minor together.</summary>
    public int TotalTickCount
    {
        get => LvglNative.lv_scale_get_total_tick_count(Handle);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            LvglNative.lv_scale_set_total_tick_count(Handle, (uint)value);
        }
    }

    /// <summary>Every n-th tick is major (longer, and labelled when <see cref="ShowLabels"/> is on).</summary>
    public int MajorTickEvery
    {
        get => LvglNative.lv_scale_get_major_tick_every(Handle);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            LvglNative.lv_scale_set_major_tick_every(Handle, (uint)value);
        }
    }

    /// <summary>Whether major ticks carry their value as a label.</summary>
    public bool ShowLabels
    {
        set => LvglNative.lv_scale_set_label_show(Handle, value);
    }

    /// <summary>Sweep of a round scale in degrees (270 is a typical gauge).</summary>
    public int AngleRange
    {
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            LvglNative.lv_scale_set_angle_range(Handle, (uint)value);
        }
    }

    /// <summary>Where a round scale starts, in degrees clockwise from 3 o'clock.</summary>
    public int Rotation
    {
        set => LvglNative.lv_scale_set_rotation(Handle, value);
    }
}
