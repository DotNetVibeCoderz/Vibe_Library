using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>A rotating arc that says "working on it".</summary>
/// <remarks>
/// The spinner animates itself on LVGL's timer, so it costs redraws for as long as it is visible:
/// hide or delete it when the work finishes rather than leaving it spinning off-screen.
/// </remarks>
public sealed class LvSpinner : LvObject
{
    /// <summary>Creates a spinner on <paramref name="parent"/>.</summary>
    public LvSpinner(LvObject? parent) : base(LvglNative.lv_spinner_create(ResolveParent(parent))) { }

    /// <summary>Sets the time for one revolution and the length of the moving arc in degrees.</summary>
    public void SetAnimation(int durationMs, int arcSweepDegrees)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationMs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arcSweepDegrees);
        LvglNative.lv_spinner_set_anim_params(Handle, (uint)durationMs, (uint)arcSweepDegrees);
    }

    /// <summary>Milliseconds per revolution.</summary>
    public int Duration => (int)LvglNative.lv_spinner_get_anim_duration(Handle);

    /// <summary>Length of the moving arc in degrees.</summary>
    public int ArcSweep => (int)LvglNative.lv_spinner_get_arc_sweep(Handle);
}
