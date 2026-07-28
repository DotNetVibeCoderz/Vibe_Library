using System.Collections.Concurrent;
using Lvgl;
using Lvgl.Backends;
using Lvgl.Interop;

namespace Lvgl.Tests;

/// <summary>
/// Owns a real LVGL instance on a dedicated thread for the integration tests.
/// </summary>
/// <remarks>
/// <para>
/// LVGL is process-global, single-threaded, and claims whichever thread initialised it. xUnit runs
/// test methods on arbitrary pool threads, so the fixture starts one thread, initialises LVGL
/// there, and marshals every test body onto it with <see cref="Invoke"/> - the same discipline a
/// real application follows via <see cref="LvglApplication.Post"/>.
/// </para>
/// <para>
/// When the native library has not been built, <see cref="IsAvailable"/> is false and the tests
/// return early rather than failing: the suite must stay runnable on a machine without CMake.
/// </para>
/// </remarks>
public sealed class LvglFixture : IDisposable
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread? _thread;
    private readonly ManualResetEventSlim _ready = new(false);

    public LvglFixture()
    {
        try
        {
            LvglNativeLibrary.Preload();
        }
        catch (LvglNativeNotFoundException ex)
        {
            UnavailableReason = ex.Message;
            return;
        }

        _thread = new Thread(Pump) { IsBackground = true, Name = "lvgl-test" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(30));
    }

    /// <summary>False when the native library is missing or failed to start.</summary>
    public bool IsAvailable => UnavailableReason is null;

    /// <summary>Why LVGL could not be started, if it could not.</summary>
    public string? UnavailableReason { get; private set; }

    public HeadlessBackend Backend { get; private set; } = null!;

    public LvglApplication Application { get; private set; } = null!;

    /// <summary>Surface size the fixture renders at.</summary>
    public const int Width = 400;

    public const int Height = 300;

    private void Pump()
    {
        try
        {
            LvglRuntime.Initialize();
            Backend = new HeadlessBackend(Width, Height);

            // Keyboard on, so the focus-group plumbing is exercised the way the samples use it.
            Application = LvglApplication.Create(Backend, new LvglOptions
            {
                EnablePointer = true,
                EnableKeyboard = true,
            });
        }
        catch (Exception ex)
        {
            UnavailableReason = ex.Message;
            _ready.Set();
            return;
        }

        _ready.Set();

        foreach (var action in _work.GetConsumingEnumerable())
        {
            action();
        }
    }

    /// <summary>Runs <paramref name="body"/> on the LVGL thread and rethrows anything it throws.</summary>
    public void Invoke(Action body)
    {
        if (!IsAvailable) throw new InvalidOperationException("LVGL is not available.");

        Exception? failure = null;
        using var done = new ManualResetEventSlim(false);

        _work.Add(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
            finally { done.Set(); }
        });

        if (!done.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("The LVGL test thread did not complete the work in time.");
        }

        if (failure is not null) throw failure;
    }

    /// <summary>
    /// Clears the screen and resets it to a known state, so each test starts from the same place.
    /// </summary>
    public void ResetScreen() => Invoke(() =>
    {
        Application.Screen.Clear();
        Application.Screen.SetBackgroundColor(Drawing.LvColor.Black);
        Application.Screen.SetPadding(0);
        Backend.SetPointer(0, 0, pressed: false);
        Backend.ClearDirty();
    });

    /// <summary>
    /// Advances LVGL until it has produced a frame, or <paramref name="maxFrames"/> is reached.
    /// LVGL only redraws when its tick has advanced, so a single RunFrame may legitimately draw
    /// nothing.
    /// </summary>
    public bool RenderFrame(int maxFrames = 40)
    {
        var rendered = false;

        Invoke(() =>
        {
            for (var i = 0; i < maxFrames && !rendered; i++)
            {
                Application.RunFrame();
                rendered = Backend.IsDirty;
                if (!rendered) Thread.Sleep(5);
            }
        });

        return rendered;
    }

    /// <summary>
    /// Types text into the focused widget, pumping enough frames for LVGL to read every key.
    /// </summary>
    /// <remarks>
    /// LVGL reads at most one key per input cycle, so each character needs its own iteration -
    /// queueing them all and running a single frame would deliver only the first.
    /// </remarks>
    public void TypeText(string text)
    {
        Invoke(() =>
        {
            Backend.SendText(text);

            // Two events per character (press and release), one read per frame.
            for (var i = 0; i < text.Length * 6 + 12 && Backend.PendingKeys > 0; i++)
            {
                Application.RunFrame();
                Thread.Sleep(5);
            }

            // A few more passes so the final key is processed and the widget redraws.
            for (var i = 0; i < 4; i++) { Application.RunFrame(); Thread.Sleep(5); }
        });
    }

    /// <summary>Sends one key code and pumps until LVGL has consumed it.</summary>
    public void SendKey(uint key)
    {
        Invoke(() =>
        {
            Backend.PressKey(key);

            for (var i = 0; i < 16 && Backend.PendingKeys > 0; i++)
            {
                Application.RunFrame();
                Thread.Sleep(5);
            }

            for (var i = 0; i < 4; i++) { Application.RunFrame(); Thread.Sleep(5); }
        });
    }

    /// <summary>Reads one pixel of the rendered surface as a packed 0xRRGGBB value.</summary>
    public uint PixelAt(int x, int y)
    {
        var offset = y * Backend.Stride + x * 4;
        var buffer = Backend.Buffer;

        // XRGB8888: byte order is B, G, R, X on little-endian.
        return ((uint)buffer[offset + 2] << 16) | ((uint)buffer[offset + 1] << 8) | buffer[offset];
    }

    public void Dispose()
    {
        if (_thread is not null)
        {
            _work.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(5));
        }

        _work.Dispose();
        _ready.Dispose();
    }
}

/// <summary>
/// Serialises every LVGL integration test onto the single fixture. LVGL's state is global, so
/// these cannot run in parallel with each other.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LvglCollection : ICollectionFixture<LvglFixture>
{
    public const string Name = "lvgl";
}
