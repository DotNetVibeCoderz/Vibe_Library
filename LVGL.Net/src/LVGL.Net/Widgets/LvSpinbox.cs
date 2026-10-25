using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>
/// A numeric field stepped digit by digit - set points, thresholds, calibration values.
/// </summary>
/// <remarks>
/// The value is an integer; <see cref="SetDigitFormat"/> places a decimal point for display, so
/// 2150 with one decimal place reads "215.0". Pair it with two buttons calling
/// <see cref="Increment"/> and <see cref="Decrement"/> for touch input.
/// </remarks>
public sealed class LvSpinbox : LvObject
{
    /// <summary>Creates a spinbox on <paramref name="parent"/>.</summary>
    public LvSpinbox(LvObject? parent) : base(LvglNative.lv_spinbox_create(ResolveParent(parent))) { }

    /// <summary>Current value, clamped by LVGL to the range.</summary>
    public int Value
    {
        get => LvglNative.lv_spinbox_get_value(Handle);
        set => LvglNative.lv_spinbox_set_value(Handle, value);
    }

    /// <summary>Sets the accepted range.</summary>
    public void SetRange(int minimum, int maximum)
    {
        if (minimum > maximum) throw new ArgumentException("minimum must not exceed maximum.", nameof(minimum));
        LvglNative.lv_spinbox_set_range(Handle, minimum, maximum);
    }

    /// <summary>
    /// Sets how many digits are shown and where the decimal point goes.
    /// </summary>
    /// <param name="digits">Total digits, 1 to 10.</param>
    /// <param name="decimalPlaces">Digits after the decimal point; 0 for an integer display.</param>
    public void SetDigitFormat(int digits, int decimalPlaces = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(digits, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, 10);
        ArgumentOutOfRangeException.ThrowIfNegative(decimalPlaces);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(decimalPlaces, digits);

        // LVGL counts the separator position from the left; 0 means "no separator".
        var separator = decimalPlaces == 0 ? 0 : digits - decimalPlaces;
        LvglNative.lv_spinbox_set_digit_format(Handle, (uint)digits, (uint)separator);
    }

    /// <summary>Amount <see cref="Increment"/> and <see cref="Decrement"/> change the value by.</summary>
    public int Step
    {
        get => LvglNative.lv_spinbox_get_step(Handle);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            LvglNative.lv_spinbox_set_step(Handle, (uint)value);
        }
    }

    /// <summary>Wraps from maximum to minimum and back instead of stopping.</summary>
    public bool Rollover
    {
        set => LvglNative.lv_spinbox_set_rollover(Handle, value);
    }

    /// <summary>Adds <see cref="Step"/>.</summary>
    public void Increment() => LvglNative.lv_spinbox_increment(Handle);

    /// <summary>Subtracts <see cref="Step"/>.</summary>
    public void Decrement() => LvglNative.lv_spinbox_decrement(Handle);
}
