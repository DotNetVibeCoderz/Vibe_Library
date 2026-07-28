using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>An on/off switch.</summary>
public sealed class LvSwitch : LvObject
{
    /// <summary>Creates a switch on <paramref name="parent"/>.</summary>
    public LvSwitch(LvObject? parent) : base(LvglNative.lv_switch_create(ResolveParent(parent))) { }

    /// <summary>
    /// Whether the switch is on. Backed by LVGL's checked state, so it can also be styled through
    /// <see cref="LvState.Checked"/>.
    /// </summary>
    public bool IsOn
    {
        get => IsChecked;
        set => IsChecked = value;
    }
}
