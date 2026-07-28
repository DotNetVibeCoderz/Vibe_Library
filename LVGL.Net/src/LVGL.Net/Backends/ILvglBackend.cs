using Lvgl.Drawing;

namespace Lvgl.Backends;

/// <summary>Current state of the pointing device (mouse, touch panel, ...).</summary>
public readonly record struct LvPointerState(int X, int Y, bool Pressed);

/// <summary>
/// Everything platform-specific that LVGL.Net needs: somewhere to put pixels and somewhere to get
/// input from.
/// </summary>
/// <remarks>
/// <para>
/// The whole point of this abstraction is that the native side stays a plain, driver-free LVGL
/// build. LVGL renders into a buffer LVGL.Net owns and calls <see cref="Blit"/>; how those pixels
/// reach a screen is entirely managed code. That is what makes the same wrapper work on an SDL
/// window, a Raspberry Pi framebuffer and an off-screen WPF preview surface without recompiling C.
/// </para>
/// <para>
/// All members are called from the LVGL UI thread only.
/// </para>
/// </remarks>
public interface ILvglBackend : IDisposable
{
    /// <summary>Width of the surface in pixels.</summary>
    int Width { get; }

    /// <summary>Height of the surface in pixels.</summary>
    int Height { get; }

    /// <summary>Pointer state sampled by the most recent <see cref="PumpEvents"/> call.</summary>
    LvPointerState Pointer { get; }

    /// <summary>
    /// Copies one rendered region to the surface.
    /// </summary>
    /// <param name="area">Destination rectangle, inclusive on both corners.</param>
    /// <param name="pixels">
    /// Pointer to the rendered region in XRGB8888 (byte order B, G, R, X on little-endian
    /// machines), tightly packed with a stride of <c>area.Width * 4</c> bytes. Only valid for the
    /// duration of the call - copy, do not retain.
    /// </param>
    /// <param name="isLastFlush">
    /// True on the final region of a frame. Backends that present through a swap chain or a
    /// double-buffered device should flip here rather than on every region.
    /// </param>
    void Blit(in LvArea area, nint pixels, bool isLastFlush);

    /// <summary>
    /// Processes host events and refreshes <see cref="Pointer"/>.
    /// </summary>
    /// <returns><see langword="false"/> when the user asked to close; the run loop then exits.</returns>
    bool PumpEvents();
}

/// <summary>Implemented by backends that can also deliver key presses to LVGL.</summary>
public interface ILvglKeyboardSource
{
    /// <summary>
    /// Dequeues one pending key event. LVGL polls one event per read cycle, so returning a single
    /// event per call is sufficient and keeps the queue in order.
    /// </summary>
    bool TryDequeueKey(out uint key, out bool pressed);
}
