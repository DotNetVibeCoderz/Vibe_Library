using Lvgl.Drawing;
using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>A round status light with adjustable colour and brightness.</summary>
public sealed class LvLed : LvObject
{
    private LvColor _color = LvColor.Blue;

    /// <summary>Creates an LED on <paramref name="parent"/>.</summary>
    public LvLed(LvObject? parent) : base(LvglNative.lv_led_create(ResolveParent(parent))) { }

    /// <summary>Light colour.</summary>
    public LvColor Color
    {
        get => _color;
        set
        {
            // lv_led_set_color takes lv_color_t by value, hence the shim (see LvglNative remarks).
            LvglNative.lvn_led_set_color(Handle, value.Rgb);
            _color = value;
        }
    }

    /// <summary>
    /// Brightness from 0 to 255. LVGL treats roughly 80 and below as "off" and draws the glow only
    /// above that.
    /// </summary>
    public byte Brightness
    {
        get => LvglNative.lv_led_get_brightness(Handle);
        set => LvglNative.lv_led_set_brightness(Handle, value);
    }

    /// <summary>Full brightness.</summary>
    public void On() => LvglNative.lv_led_on(Handle);

    /// <summary>Minimum brightness.</summary>
    public void Off() => LvglNative.lv_led_off(Handle);

    /// <summary>Flips between on and off.</summary>
    public void Toggle() => LvglNative.lv_led_toggle(Handle);
}
