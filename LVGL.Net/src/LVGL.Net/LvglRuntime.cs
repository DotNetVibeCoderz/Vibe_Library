using Lvgl.Interop;

namespace Lvgl;

/// <summary>
/// Process-wide LVGL state: initialisation, version checks and UI-thread affinity.
/// </summary>
/// <remarks>
/// LVGL keeps its object tree, style cache and timer list in globals guarded by no lock at all, so
/// exactly one thread may touch it. LVGL.Net enforces that by recording the thread that called
/// <see cref="Initialize"/> and checking it on the operations most likely to be called from a
/// worker (see <see cref="EnsureUiThread"/>). Background work should go through
/// <see cref="LvglApplication.Post"/> instead.
/// </remarks>
public static class LvglRuntime
{
    private static readonly Lock Gate = new();
    private static int _uiThreadId = -1;
    private static bool _initialized;

    /// <summary>Minimum LVGL version this wrapper is written against.</summary>
    public static Version MinimumLvglVersion { get; } = new(9, 0, 0);

    /// <summary>
    /// ABI version the managed side expects from the native shim. Kept in step with
    /// <c>LVN_ABI_VERSION</c> in <c>native/shim/lvglnet_shim.h</c>.
    /// </summary>
    public const uint ExpectedShimAbi = 3;

    /// <summary>True once <see cref="Initialize"/> has completed.</summary>
    public static bool IsInitialized
    {
        get { lock (Gate) return _initialized; }
    }

    /// <summary>Version of the LVGL build that was actually loaded.</summary>
    public static Version LvglVersion
    {
        get
        {
            var packed = LvglNative.lvn_lvgl_version();
            return new Version((int)(packed >> 16), (int)((packed >> 8) & 0xFF), (int)(packed & 0xFF));
        }
    }

    /// <summary>Colour depth the native library was compiled with. LVGL.Net requires 32.</summary>
    public static uint ColorDepth => LvglNative.lvn_color_depth();

    /// <summary>
    /// Loads the native library, validates it and calls <c>lv_init</c>. Safe to call repeatedly;
    /// only the first call does work. The calling thread becomes the UI thread.
    /// </summary>
    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized) return;

            LvglNativeLibrary.Preload();

            var abi = LvglNative.lvn_abi_version();
            if (abi != ExpectedShimAbi)
            {
                throw new LvglException(
                    $"Native shim ABI {abi} does not match the expected {ExpectedShimAbi}. " +
                    "Rebuild the native library with native/build.sh or native/build.ps1.");
            }

            var version = LvglVersion;
            if (version < MinimumLvglVersion)
            {
                throw new LvglException(
                    $"LVGL {version} is too old; LVGL.Net targets the v9 API ({MinimumLvglVersion} or newer).");
            }

            // The backends blit raw XRGB8888 words. A 16-bit build would silently produce
            // garbled output, so refuse it rather than draw nonsense.
            var depth = ColorDepth;
            if (depth != 32)
            {
                throw new LvglException(
                    $"The native library was built with LV_COLOR_DEPTH={depth}. LVGL.Net requires 32; " +
                    "check native/lv_conf.h.");
            }

            if (!LvglNative.lv_is_initialized()) LvglNative.lv_init();

            _uiThreadId = Environment.CurrentManagedThreadId;
            _initialized = true;
        }
    }

    /// <summary>
    /// Throws when called from a thread other than the one that initialised LVGL.
    /// </summary>
    /// <exception cref="LvglThreadAccessException">The caller is not the UI thread.</exception>
    public static void EnsureUiThread()
    {
        var owner = Volatile.Read(ref _uiThreadId);
        if (owner == -1 || owner == Environment.CurrentManagedThreadId) return;

        throw new LvglThreadAccessException(
            $"LVGL was accessed from thread {Environment.CurrentManagedThreadId} but is owned by thread {owner}. " +
            "Use LvglApplication.Post(...) to run UI work on the LVGL thread.");
    }

    /// <summary>True when the current thread owns LVGL.</summary>
    public static bool IsUiThread =>
        Volatile.Read(ref _uiThreadId) is var owner && (owner == -1 || owner == Environment.CurrentManagedThreadId);

    /// <summary>
    /// Tears LVGL down. Intended for tests and for hosts that create and destroy the UI repeatedly;
    /// long-running applications normally just exit the process.
    /// </summary>
    public static void Shutdown()
    {
        lock (Gate)
        {
            if (!_initialized) return;
            LvglNative.lv_deinit();
            _initialized = false;
            _uiThreadId = -1;
        }
    }
}

/// <summary>Error raised by the LVGL.Net wrapper.</summary>
public class LvglException : Exception
{
    public LvglException(string message) : base(message) { }

    public LvglException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Thrown when LVGL is touched from a thread that does not own it.</summary>
public sealed class LvglThreadAccessException(string message) : LvglException(message);
