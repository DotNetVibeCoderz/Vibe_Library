using System.Runtime.InteropServices;
using Lvgl.Drawing;

namespace Lvgl.Backends;

/// <summary>
/// Renders into a managed pixel buffer instead of a screen.
/// </summary>
/// <remarks>
/// Two things use this: the WPF designer, which copies <see cref="Frame"/> into a
/// <c>WriteableBitmap</c> to show a true LVGL preview, and the test suite, which asserts on
/// rendered pixels without needing a display server. Input can be driven synthetically through
/// <see cref="SetPointer"/>.
/// </remarks>
public sealed class HeadlessBackend : ILvglBackend, ILvglKeyboardSource
{
    private readonly byte[] _frame;
    private readonly Queue<(uint Key, bool Pressed)> _keys = new();
    private LvPointerState _pointer;

    /// <summary>Creates an off-screen surface.</summary>
    public HeadlessBackend(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        Stride = width * BytesPerPixel;
        _frame = new byte[Stride * height];
    }

    /// <summary>Bytes per pixel in <see cref="Frame"/> (XRGB8888).</summary>
    public const int BytesPerPixel = 4;

    public int Width { get; }

    public int Height { get; }

    /// <summary>Bytes per row of <see cref="Frame"/>.</summary>
    public int Stride { get; }

    /// <summary>
    /// The rendered surface in XRGB8888, laid out top-down with no row padding beyond
    /// <see cref="Stride"/>. Valid to read between run-loop iterations.
    /// </summary>
    public ReadOnlySpan<byte> Frame => _frame;

    /// <summary>The backing array, for APIs that need one (e.g. <c>WriteableBitmap.WritePixels</c>).</summary>
    public byte[] Buffer => _frame;

    public LvPointerState Pointer => _pointer;

    /// <summary>True when at least one region was blitted since <see cref="ClearDirty"/>.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Union of the regions blitted since <see cref="ClearDirty"/>.</summary>
    public LvArea DirtyArea { get; private set; }

    /// <summary>Raised on the UI thread after the final region of a frame has been blitted.</summary>
    public event EventHandler? FrameRendered;

    /// <summary>Injects pointer state, as though a mouse or touch panel had reported it.</summary>
    public void SetPointer(int x, int y, bool pressed) => _pointer = new LvPointerState(x, y, pressed);

    /// <summary>
    /// Queues a key event, as though a keyboard had reported it.
    /// </summary>
    /// <remarks>
    /// LVGL polls one event per read cycle, so each queued press needs a run-loop iteration to be
    /// consumed. Printable characters are their ASCII value; navigation and editing use LVGL's
    /// <c>LV_KEY_*</c> codes (8 backspace, 10 enter, 27 escape, 127 delete, 17-20 arrows).
    /// </remarks>
    public void SendKey(uint key, bool pressed = true) => _keys.Enqueue((key, pressed));

    /// <summary>
    /// Queues a complete keystroke: a press followed by a release.
    /// </summary>
    /// <remarks>
    /// The release is not optional. LVGL only registers a new key when the device transitions
    /// from released to pressed, so a run of presses with no release between them is read as one
    /// key still being held and every character after the first is dropped.
    /// </remarks>
    public void PressKey(uint key)
    {
        _keys.Enqueue((key, true));
        _keys.Enqueue((key, false));
    }

    /// <summary>Queues a full keystroke per character of <paramref name="text"/>.</summary>
    public void SendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var character in text) PressKey(character);
    }

    /// <summary>Number of key events still waiting to be read by LVGL.</summary>
    public int PendingKeys => _keys.Count;

    /// <inheritdoc />
    public bool TryDequeueKey(out uint key, out bool pressed)
    {
        if (_keys.TryDequeue(out var entry))
        {
            (key, pressed) = entry;
            return true;
        }

        key = 0;
        pressed = false;
        return false;
    }

    /// <summary>Resets <see cref="IsDirty"/> and <see cref="DirtyArea"/> after consuming a frame.</summary>
    public void ClearDirty()
    {
        IsDirty = false;
        DirtyArea = default;
    }

    public void Blit(in LvArea area, nint pixels, bool isLastFlush)
    {
        var clipped = area.Clip(new LvArea(0, 0, Width - 1, Height - 1));
        if (!clipped.IsEmpty)
        {
            // LVGL renders the region tightly packed, so the source stride is the region's own
            // width - not the screen width. Copy row by row into the full-size frame.
            var sourceStride = area.Width * BytesPerPixel;
            var rowBytes = clipped.Width * BytesPerPixel;
            var leadingBytes = (clipped.X1 - area.X1) * BytesPerPixel;

            unsafe
            {
                var source = (byte*)pixels;
                for (var y = clipped.Y1; y <= clipped.Y2; y++)
                {
                    var sourceRow = source + (y - area.Y1) * sourceStride + leadingBytes;
                    var destination = _frame.AsSpan(y * Stride + clipped.X1 * BytesPerPixel, rowBytes);
                    new ReadOnlySpan<byte>(sourceRow, rowBytes).CopyTo(destination);
                }
            }

            DirtyArea = IsDirty
                ? new LvArea(
                    Math.Min(DirtyArea.X1, clipped.X1),
                    Math.Min(DirtyArea.Y1, clipped.Y1),
                    Math.Max(DirtyArea.X2, clipped.X2),
                    Math.Max(DirtyArea.Y2, clipped.Y2))
                : clipped;

            IsDirty = true;
        }

        if (isLastFlush) FrameRendered?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Always true - there is no window to close.</summary>
    public bool PumpEvents() => true;

    /// <summary>
    /// Copies the frame into a caller-supplied buffer, converting nothing. The destination must be
    /// at least <see cref="Stride"/> * <see cref="Height"/> bytes.
    /// </summary>
    public void CopyTo(Span<byte> destination) => _frame.CopyTo(destination);

    /// <summary>Copies the frame to unmanaged memory, e.g. a locked bitmap's back buffer.</summary>
    public void CopyTo(nint destination, int destinationStride)
    {
        if (destinationStride == Stride)
        {
            Marshal.Copy(_frame, 0, destination, _frame.Length);
            return;
        }

        for (var y = 0; y < Height; y++)
        {
            Marshal.Copy(_frame, y * Stride, destination + y * destinationStride, Stride);
        }
    }

    public void Dispose() { }
}
