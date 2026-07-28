using Lvgl.Backends;
using Lvgl.Interop;

namespace Lvgl.Tests;

/// <summary>
/// Behaviour of the wrapper when the native library is absent.
/// </summary>
/// <remarks>
/// The whole test suite is deliberately runnable without a compiled <c>lvglnet</c>: CI, and any
/// contributor who has not installed CMake, should still be able to run <c>dotnet test</c>. These
/// tests assert that the failure is a clear, actionable message rather than an opaque
/// <see cref="DllNotFoundException"/> from somewhere deep in a widget constructor.
/// </remarks>
public class NativeLibraryTests
{
    private static bool NativeAvailable
    {
        get
        {
            try
            {
                LvglNativeLibrary.Preload();
                return true;
            }
            catch (LvglNativeNotFoundException)
            {
                return false;
            }
        }
    }

    [Fact]
    public void A_missing_native_library_explains_how_to_build_it()
    {
        if (NativeAvailable) return;   // nothing to assert when the library is present

        var error = Assert.Throws<LvglNativeNotFoundException>(LvglNativeLibrary.Preload);

        Assert.Contains("native/build", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(LvglNativeLibrary.PathVariable, error.Message, StringComparison.Ordinal);
        Assert.NotEmpty(LvglNativeLibrary.ProbedPaths);
    }

    [Fact]
    public void Creating_an_application_without_the_native_library_fails_before_touching_the_backend()
    {
        if (NativeAvailable) return;

        using var backend = new HeadlessBackend(64, 48);

        Assert.Throws<LvglNativeNotFoundException>(() => LvglApplication.Create(backend));
    }

    [Fact]
    public void Backend_geometry_is_validated_up_front()
    {
        // Checked before LVGL is touched, so the message is about the backend rather than an
        // lv_display_create failure.
        using var backend = new HeadlessBackend(1, 1);
        Assert.NotNull(backend);

        Assert.Throws<ArgumentOutOfRangeException>(() => new HeadlessBackend(0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeadlessBackend(100, -1));
    }

    [Fact]
    public void Headless_backend_reports_a_tightly_packed_xrgb_surface()
    {
        using var backend = new HeadlessBackend(120, 40);

        Assert.Equal(120 * 4, backend.Stride);
        Assert.Equal(120 * 40 * 4, backend.Buffer.Length);
        Assert.False(backend.IsDirty);
        Assert.True(backend.PumpEvents());
    }

    [Fact]
    public void Headless_backend_accepts_synthetic_pointer_input()
    {
        using var backend = new HeadlessBackend(100, 100);

        backend.SetPointer(12, 34, pressed: true);

        Assert.Equal(new LvPointerState(12, 34, true), backend.Pointer);
    }
}
