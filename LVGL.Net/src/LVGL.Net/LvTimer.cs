using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lvgl.Interop;

namespace Lvgl;

/// <summary>
/// Runs a callback periodically on the LVGL thread, inside the run loop.
/// </summary>
/// <remarks>
/// <para>
/// The UI-thread counterpart to a <see cref="System.Threading.Timer"/> plus
/// <see cref="LvglApplication.Post"/>: the tick runs between frames with LVGL already owned, so
/// it may touch widgets directly and needs no queue. Use it for clocks, blinking indicators and
/// polling cheap state. Work that blocks belongs on a background thread instead - a slow tick
/// stalls rendering.
/// </para>
/// <para>
/// Ticks are timed by LVGL's clock, so a timer only advances while the run loop does.
/// </para>
/// <para>
/// <b>Lifetime.</b> Dispose the timer to stop and free it. A running timer is kept alive by
/// LVGL.Net, so a fire-and-forget timer is not collected. LVGL's own auto-delete is switched off:
/// it would free the timer behind the wrapper's back when <see cref="RepeatCount"/> ran out, so
/// instead an exhausted timer pauses and waits to be disposed or restarted.
/// </para>
/// </remarks>
public sealed unsafe class LvTimer : IDisposable
{
    /// <summary>Pass as <see cref="RepeatCount"/> to repeat forever (the default).</summary>
    public const int Infinite = -1;

    private static readonly Dictionary<nint, LvTimer> Live = [];
    private static long _nextId;

    private readonly Action<LvTimer> _tick;
    private readonly nint _id;
    private nint _handle;
    private int _period;

    /// <summary>Creates and starts a timer.</summary>
    /// <param name="periodMs">Milliseconds between ticks.</param>
    /// <param name="tick">Runs on the UI thread at each tick.</param>
    /// <param name="repeatCount">Number of ticks before pausing; <see cref="Infinite"/> to keep going.</param>
    public LvTimer(int periodMs, Action<LvTimer> tick, int repeatCount = Infinite)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(periodMs);
        ArgumentNullException.ThrowIfNull(tick);
        if (repeatCount is 0 or < Infinite) throw new ArgumentOutOfRangeException(nameof(repeatCount), "Use a positive count or LvTimer.Infinite.");

        LvglRuntime.EnsureUiThread();

        _tick = tick;
        _period = periodMs;
        _id = (nint)Interlocked.Increment(ref _nextId);

        _handle = LvglNative.lv_timer_create(&TickThunk, (uint)periodMs, (void*)_id);
        if (_handle == 0) throw new LvglException("LVGL could not create a timer.");

        LvglNative.lv_timer_set_auto_delete(_handle, false);
        LvglNative.lv_timer_set_repeat_count(_handle, repeatCount);
        RepeatCount = repeatCount;

        Live[_id] = this;
    }

    /// <summary>Number of ticks so far.</summary>
    public long TickCount { get; private set; }

    /// <summary>The repeat count the timer was last started with.</summary>
    public int RepeatCount { get; private set; }

    /// <summary>Milliseconds between ticks.</summary>
    public int Period
    {
        get => _period;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            LvglNative.lv_timer_set_period(Handle, (uint)value);
            _period = value;
        }
    }

    /// <summary>True when paused, by <see cref="Pause"/> or because the repeat count ran out.</summary>
    public bool IsPaused => LvglNative.lv_timer_get_paused(Handle);

    /// <summary>False once disposed.</summary>
    public bool IsAlive => _handle != 0;

    private nint Handle => _handle != 0 ? _handle : throw new ObjectDisposedException(nameof(LvTimer));

    /// <summary>Suspends ticking.</summary>
    public void Pause() => LvglNative.lv_timer_pause(Handle);

    /// <summary>Resumes ticking.</summary>
    public void Resume() => LvglNative.lv_timer_resume(Handle);

    /// <summary>Makes the next tick happen on the next run-loop iteration.</summary>
    public void FireSoon() => LvglNative.lv_timer_ready(Handle);

    /// <summary>Restarts the period from now.</summary>
    public void ResetPeriod() => LvglNative.lv_timer_reset(Handle);

    /// <summary>Restarts an exhausted or paused timer with a new repeat count.</summary>
    public void Restart(int repeatCount = Infinite)
    {
        if (repeatCount is 0 or < Infinite) throw new ArgumentOutOfRangeException(nameof(repeatCount));
        LvglNative.lv_timer_set_repeat_count(Handle, repeatCount);
        RepeatCount = repeatCount;
        LvglNative.lv_timer_reset(Handle);
        LvglNative.lv_timer_resume(Handle);
    }

    /// <summary>Stops and frees the timer.</summary>
    public void Dispose()
    {
        if (_handle == 0) return;
        LvglRuntime.EnsureUiThread();

        LvglNative.lv_timer_delete(_handle);
        _handle = 0;
        Live.Remove(_id);
    }

    /// <summary>Number of live timers created through LVGL.Net.</summary>
    public static int LiveCount => Live.Count;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void TickThunk(nint timer)
    {
        try
        {
            var id = (nint)LvglNative.lv_timer_get_user_data(timer);
            if (!Live.TryGetValue(id, out var self)) return;

            self.TickCount++;
            self._tick(self);
        }
        catch (Exception ex)
        {
            LvglRuntime.ReportCallbackFailure("timer", ex);
        }
    }
}
