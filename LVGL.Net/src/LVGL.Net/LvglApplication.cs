using System.Collections.Concurrent;
using System.Diagnostics;
using Lvgl.Backends;
using Lvgl.Display;
using Lvgl.Input;
using Lvgl.Interop;
using Lvgl.Widgets;

namespace Lvgl;

/// <summary>
/// Owns the LVGL run loop: the tick source, the timer handler, the display and the input devices.
/// </summary>
/// <example>
/// <code>
/// using var backend = new SdlBackend(800, 480, "Demo");
/// using var app = LvglApplication.Create(backend);
///
/// var button = new LvButton(app.Screen) { Text = "Tap me" };
/// button.Center();
/// button.Clicked += (_, _) => Console.WriteLine("clicked");
///
/// app.Run();
/// </code>
/// </example>
public sealed class LvglApplication : IDisposable
{
    private readonly ILvglBackend _backend;
    private readonly LvglOptions _options;
    private readonly LvglInputDevice? _pointer;
    private readonly LvglInputDevice? _keypad;
    private readonly ConcurrentQueue<Action> _pending = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastTickMs;
    private volatile bool _stopRequested;
    private bool _disposed;

    private LvglApplication(ILvglBackend backend, LvglOptions options)
    {
        _backend = backend;
        _options = options;

        Display = new LvglDisplay(backend, options.RenderMode, options.ResolveBufferLines(backend.Height));

        if (options.EnablePointer)
        {
            _pointer = new LvglInputDevice(backend, LvIndevType.Pointer, Display.Handle);
        }

        if (options.EnableKeyboard)
        {
            // The group must exist and be the default before any widget is built, because LVGL
            // adds focusable widgets to the default group as it constructs them. A keypad device
            // with no group has nowhere to deliver key events, and typing does nothing at all.
            FocusGroup = LvGroup.CreateDefault();

            _keypad = new LvglInputDevice(backend, LvIndevType.Keypad, Display.Handle);
            _keypad.SetGroup(FocusGroup);
        }

        Screen = LvScreen.Active();
    }

    /// <summary>
    /// Initialises LVGL if needed and wires it to <paramref name="backend"/>. The calling thread
    /// becomes the UI thread; every widget call must happen on it (see <see cref="Post"/>).
    /// </summary>
    public static LvglApplication Create(ILvglBackend backend, LvglOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(backend);

        if (backend.Width <= 0 || backend.Height <= 0)
        {
            throw new ArgumentException($"Backend reported an invalid surface size {backend.Width}x{backend.Height}.", nameof(backend));
        }

        LvglRuntime.Initialize();
        return new LvglApplication(backend, options ?? new LvglOptions());
    }

    /// <summary>The active screen, i.e. the root object new widgets are usually parented to.</summary>
    public LvScreen Screen { get; }

    /// <summary>The display this application renders through.</summary>
    public LvglDisplay Display { get; }

    /// <summary>The backend supplying the surface and input.</summary>
    public ILvglBackend Backend => _backend;

    /// <summary>
    /// The keyboard focus group, or null when <see cref="LvglOptions.EnableKeyboard"/> is off.
    /// Focusable widgets join it automatically as they are created.
    /// </summary>
    public LvGroup? FocusGroup { get; }

    /// <summary>Frames completed since the loop started.</summary>
    public long FrameCount { get; private set; }

    /// <summary>Raised on the UI thread after each run-loop iteration.</summary>
    public event EventHandler? FrameCompleted;

    /// <summary>
    /// Queues <paramref name="action"/> to run on the UI thread at the start of the next
    /// iteration. This is the supported way for background threads - a sensor poller, a network
    /// client - to touch widgets.
    /// </summary>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _pending.Enqueue(action);
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread and waits for it to finish.
    /// </summary>
    /// <remarks>
    /// Deadlocks if called from the UI thread while the loop is not running, so it short-circuits
    /// and invokes directly in that case.
    /// </remarks>
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (LvglRuntime.IsUiThread)
        {
            action();
            return;
        }

        using var done = new ManualResetEventSlim(false);
        ExceptionDispatchInfoHolder holder = new();

        _pending.Enqueue(() =>
        {
            try { action(); }
            catch (Exception ex) { holder.Exception = ex; }
            finally { done.Set(); }
        });

        done.Wait();
        if (holder.Exception is not null)
        {
            throw new LvglException("An action posted to the LVGL thread failed.", holder.Exception);
        }
    }

    private sealed class ExceptionDispatchInfoHolder
    {
        public Exception? Exception;
    }

    /// <summary>
    /// Runs the loop until the backend reports a close request, <see cref="Stop"/> is called or
    /// <paramref name="cancellationToken"/> fires.
    /// </summary>
    public void Run(CancellationToken cancellationToken = default)
    {
        LvglRuntime.EnsureUiThread();
        ObjectDisposedException.ThrowIf(_disposed, this);

        _stopRequested = false;
        _lastTickMs = _clock.ElapsedMilliseconds;

        while (!_stopRequested && !cancellationToken.IsCancellationRequested)
        {
            if (!RunFrame()) break;

            var sleep = Math.Clamp(NextIdleMs, _options.MinIdleSleepMs, _options.MaxIdleSleepMs);
            if (sleep > 0) Thread.Sleep(sleep);
        }
    }

    /// <summary>Idle time in milliseconds reported by the most recent timer pass.</summary>
    public int NextIdleMs { get; private set; }

    /// <summary>
    /// Advances LVGL by one iteration: drains posted work, feeds the tick, runs timers and
    /// redraws, then pumps host events.
    /// </summary>
    /// <returns><see langword="false"/> when the backend asked to close.</returns>
    public bool RunFrame()
    {
        LvglRuntime.EnsureUiThread();

        while (_pending.TryDequeue(out var work)) work();

        // LVGL needs a monotonic millisecond tick. Feeding it the real elapsed delta (rather than
        // a fixed increment) keeps animations correct when a frame runs long.
        var now = _clock.ElapsedMilliseconds;
        var elapsed = now - _lastTickMs;
        if (elapsed > 0)
        {
            _lastTickMs = now;
            LvglNative.lv_tick_inc((uint)elapsed);
        }

        var idle = LvglNative.lv_timer_handler();
        NextIdleMs = idle > int.MaxValue ? int.MaxValue : (int)idle;

        // Surfaces anything the backend threw while LVGL was calling back into managed code.
        Display.ThrowPendingCallbackException();

        var keepRunning = _backend.PumpEvents();

        FrameCount++;
        FrameCompleted?.Invoke(this, EventArgs.Empty);

        return keepRunning;
    }

    /// <summary>Asks <see cref="Run"/> to return after the current iteration.</summary>
    public void Stop() => _stopRequested = true;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _keypad?.Dispose();
        _pointer?.Dispose();

        // After the devices, so nothing is still pointing at the group when it goes.
        FocusGroup?.Dispose();

        Display.Dispose();
    }
}
