using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lvgl.Interop;
using Lvgl.Widgets;

namespace Lvgl;

/// <summary>
/// Animates an integer from one value to another over time, driven by LVGL's own animation timer.
/// </summary>
/// <remarks>
/// <para>
/// Each frame LVGL computes the eased value and calls <see cref="Apply"/> with it, so anything an
/// integer can drive can be animated: a position, an opacity, a bar's value, a colour component.
/// The <c>Animate*</c> helpers on <see cref="LvAnimations"/> cover the common widget properties.
/// </para>
/// <para>
/// <b>Lifetime.</b> A running animation is kept alive by LVGL.Net, so it does not need to be held
/// in a field. When <see cref="Target"/> is set, deleting that widget stops the animation - without
/// it, the next frame would call <see cref="Apply"/> on a deleted widget.
/// </para>
/// <para>
/// <b>Routing.</b> LVGL identifies the animation by an opaque id rather than a widget pointer, and
/// the callbacks find the instance through a registry - the same reasoning as widget events: a
/// stale id is a missed lookup, never a read of freed memory. Stopping one animation therefore
/// never stops another running on the same widget.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// new LvAnimation(card, x => card.X = x, from: -200, to: 16)
/// {
///     Duration = 400,
///     Path = LvAnimPath.EaseOut,
/// }.Start();
/// </code>
/// </example>
public sealed class LvAnimation
{
    /// <summary>Pass as <see cref="RepeatCount"/> to repeat forever.</summary>
    public const int Infinite = -1;

    private static readonly Dictionary<nint, LvAnimation> Running = [];
    private static long _nextId;
    private static bool _callbacksInstalled;

    private readonly nint _id;

    /// <summary>Creates an animation; nothing moves until <see cref="Start"/>.</summary>
    /// <param name="target">Widget the animation belongs to; it stops when the widget is deleted. May be null.</param>
    /// <param name="apply">Receives each frame's value. Runs on the UI thread, once per frame - keep it cheap.</param>
    /// <param name="from">Start value.</param>
    /// <param name="to">End value.</param>
    public LvAnimation(LvObject? target, Action<int> apply, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(apply);

        Target = target;
        Apply = apply;
        From = from;
        To = to;

        // Never zero: LVGL treats a NULL var as "no animation".
        _id = (nint)Interlocked.Increment(ref _nextId);
    }

    /// <summary>The widget the animation belongs to, if any.</summary>
    public LvObject? Target { get; }

    /// <summary>Receives each frame's value.</summary>
    public Action<int> Apply { get; }

    /// <summary>Start value.</summary>
    public int From { get; set; }

    /// <summary>End value.</summary>
    public int To { get; set; }

    /// <summary>Milliseconds from <see cref="From"/> to <see cref="To"/>. Default 300.</summary>
    public int Duration { get; set; } = 300;

    /// <summary>Milliseconds to wait before starting.</summary>
    public int Delay { get; set; }

    /// <summary>Easing curve. Default <see cref="LvAnimPath.EaseInOut"/>.</summary>
    public LvAnimPath Path { get; set; } = LvAnimPath.EaseInOut;

    /// <summary>
    /// When greater than zero, the animation plays back from <see cref="To"/> to <see cref="From"/>
    /// over this many milliseconds after each forward run.
    /// </summary>
    public int ReverseDuration { get; set; }

    /// <summary>Milliseconds to pause at <see cref="To"/> before playing back.</summary>
    public int ReverseDelay { get; set; }

    /// <summary>
    /// Total number of runs; 1 (the default) plays once, <see cref="Infinite"/> forever.
    /// </summary>
    public int RepeatCount { get; set; } = 1;

    /// <summary>Milliseconds between repeats.</summary>
    public int RepeatDelay { get; set; }

    /// <summary>
    /// Applies <see cref="From"/> immediately on <see cref="Start"/> even when there is a
    /// <see cref="Delay"/>, so the widget does not jump when the delay ends. Default true.
    /// </summary>
    public bool ApplyFromImmediately { get; set; } = true;

    /// <summary>True between <see cref="Start"/> and the animation finishing or being stopped.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Raised when the animation finishes on its own; not raised by <see cref="Stop"/>.</summary>
    public event EventHandler? Completed;

    /// <summary>Starts (or restarts) the animation.</summary>
    /// <returns>This instance, for chaining.</returns>
    public LvAnimation Start()
    {
        LvglRuntime.EnsureUiThread();
        if (Target is { IsAlive: false }) throw new ObjectDisposedException(Target.GetType().Name, "The animation target has been deleted.");

        ArgumentOutOfRangeException.ThrowIfNegative(Duration);
        ArgumentOutOfRangeException.ThrowIfNegative(Delay);
        ArgumentOutOfRangeException.ThrowIfNegative(ReverseDuration);
        ArgumentOutOfRangeException.ThrowIfNegative(ReverseDelay);
        ArgumentOutOfRangeException.ThrowIfNegative(RepeatDelay);
        if (RepeatCount is 0 or < Infinite) throw new ArgumentOutOfRangeException(nameof(RepeatCount), "Use a positive count or LvAnimation.Infinite.");

        EnsureCallbacks();

        // Restarting: stop the old run first. LVGL calls the deleted callback synchronously, so the
        // registry is consistent again before the new run is added.
        if (IsRunning) Stop();

        Running[_id] = this;
        IsRunning = true;

        var repeat = RepeatCount == Infinite ? uint.MaxValue : (uint)RepeatCount;
        var started = LvglNative.lvn_anim_start(
            _id, From, To, (uint)Duration, (uint)Delay, (uint)ReverseDuration, (uint)ReverseDelay,
            repeat, (uint)RepeatDelay, (int)Path, ApplyFromImmediately ? (byte)1 : (byte)0);

        if (started == 0)
        {
            Running.Remove(_id);
            IsRunning = false;
            throw new LvglException("LVGL could not start the animation.");
        }

        return this;
    }

    /// <summary>Stops the animation where it is. Does nothing when it is not running.</summary>
    public void Stop()
    {
        if (!IsRunning) return;
        LvglRuntime.EnsureUiThread();

        LvglNative.lvn_anim_stop(_id);

        // The deleted callback normally clears these; do it here too in case LVGL had already
        // dropped the animation without telling us.
        Running.Remove(_id);
        IsRunning = false;
    }

    /// <summary>Number of animations currently running through LVGL.Net.</summary>
    public static int RunningCount => Running.Count;

    /// <summary>Stops every animation whose <see cref="Target"/> is <paramref name="target"/>.</summary>
    internal static void StopAllFor(LvObject target)
    {
        if (Running.Count == 0) return;

        List<LvAnimation>? doomed = null;
        foreach (var animation in Running.Values)
        {
            if (ReferenceEquals(animation.Target, target)) (doomed ??= []).Add(animation);
        }

        if (doomed is null) return;
        foreach (var animation in doomed) animation.Stop();
    }

    private static unsafe void EnsureCallbacks()
    {
        if (_callbacksInstalled) return;
        LvglNative.lvn_anim_set_callbacks(&ExecThunk, &CompletedThunk, &DeletedThunk);
        _callbacksInstalled = true;
    }

    /// <summary>Called by LVGL every frame with the eased value.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ExecThunk(nint id, int value)
    {
        try
        {
            if (!Running.TryGetValue(id, out var animation)) return;

            // Belt and braces: StopAllFor should already have stopped it when the target died.
            if (animation.Target is { IsAlive: false })
            {
                animation.Stop();
                return;
            }

            animation.Apply(value);
        }
        catch (Exception ex)
        {
            LvglRuntime.ReportCallbackFailure("animation", ex);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CompletedThunk(nint id)
    {
        try
        {
            if (Running.TryGetValue(id, out var animation)) animation.Completed?.Invoke(animation, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            LvglRuntime.ReportCallbackFailure("animation completed", ex);
        }
    }

    /// <summary>Called whenever LVGL frees an animation - finished or stopped.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DeletedThunk(nint id)
    {
        try
        {
            if (Running.Remove(id, out var animation)) animation.IsRunning = false;
        }
        catch (Exception ex)
        {
            LvglRuntime.ReportCallbackFailure("animation deleted", ex);
        }
    }
}

/// <summary>Shortcuts for animating common widget properties.</summary>
public static class LvAnimations
{
    /// <summary>Slides the widget horizontally from its current X to <paramref name="to"/>.</summary>
    public static LvAnimation AnimateX(this LvObject widget, int to, int durationMs = 300, LvAnimPath path = LvAnimPath.EaseInOut) =>
        Run(widget, value => widget.X = value, widget.X, to, durationMs, path);

    /// <summary>Slides the widget vertically from its current Y to <paramref name="to"/>.</summary>
    public static LvAnimation AnimateY(this LvObject widget, int to, int durationMs = 300, LvAnimPath path = LvAnimPath.EaseInOut) =>
        Run(widget, value => widget.Y = value, widget.Y, to, durationMs, path);

    /// <summary>Resizes the widget's width from its current value to <paramref name="to"/>.</summary>
    public static LvAnimation AnimateWidth(this LvObject widget, int to, int durationMs = 300, LvAnimPath path = LvAnimPath.EaseInOut) =>
        Run(widget, value => widget.Width = value, widget.Width, to, durationMs, path);

    /// <summary>Resizes the widget's height from its current value to <paramref name="to"/>.</summary>
    public static LvAnimation AnimateHeight(this LvObject widget, int to, int durationMs = 300, LvAnimPath path = LvAnimPath.EaseInOut) =>
        Run(widget, value => widget.Height = value, widget.Height, to, durationMs, path);

    /// <summary>Fades the whole widget between two opacities (0-255).</summary>
    public static LvAnimation AnimateOpacity(this LvObject widget, byte from, byte to, int durationMs = 300, LvAnimPath path = LvAnimPath.EaseInOut) =>
        Run(widget, value => widget.SetOpacity((byte)Math.Clamp(value, 0, 255)), from, to, durationMs, path);

    /// <summary>Animates a bar's value from its current value to <paramref name="to"/>.</summary>
    public static LvAnimation AnimateValue(this LvBar bar, int to, int durationMs = 300, LvAnimPath path = LvAnimPath.EaseInOut) =>
        Run(bar, value => bar.SetValue(value, animate: false), bar.Value, to, durationMs, path);

    /// <summary>Animates an arc's value from its current value to <paramref name="to"/>.</summary>
    public static LvAnimation AnimateValue(this LvArc arc, int to, int durationMs = 300, LvAnimPath path = LvAnimPath.EaseInOut) =>
        Run(arc, value => arc.Value = value, arc.Value, to, durationMs, path);

    private static LvAnimation Run(LvObject widget, Action<int> apply, int from, int to, int durationMs, LvAnimPath path)
    {
        ArgumentNullException.ThrowIfNull(widget);

        return new LvAnimation(widget, apply, from, to)
        {
            Duration = durationMs,
            Path = path,
        }.Start();
    }
}
