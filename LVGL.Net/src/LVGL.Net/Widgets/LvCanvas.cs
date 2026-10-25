using System.Runtime.InteropServices;
using Lvgl.Drawing;
using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>
/// A widget showing a pixel buffer that managed code draws into directly: plots, heat maps,
/// fractals, game boards, waveforms - anything that is cheaper as pixels than as widgets.
/// </summary>
/// <remarks>
/// <para>
/// The buffer is <c>0xAARRGGBB</c> per pixel (LVGL's ARGB8888), allocated in unmanaged memory and
/// exposed as <see cref="Pixels"/>. Draw into it - with the helpers here or by writing the span -
/// and call <see cref="LvObject.Invalidate"/> once the frame is complete; LVGL redraws the canvas on
/// its next refresh. Nothing on that path allocates.
/// </para>
/// <para>
/// The buffer belongs to the canvas and is freed when LVGL deletes it, so unlike
/// <see cref="LvImageSource"/> there is nothing to dispose. Do not keep a <see cref="Pixels"/>
/// span across a deletion.
/// </para>
/// </remarks>
public sealed unsafe class LvCanvas : LvObject
{
    private uint* _pixels;

    /// <summary>Creates a canvas of <paramref name="width"/> × <paramref name="height"/> pixels, cleared to transparent.</summary>
    public LvCanvas(LvObject? parent, int width, int height) : base(LvglNative.lv_canvas_create(ResolveParent(parent)))
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 0x3FFF);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, 0x3FFF);

        BufferWidth = width;
        BufferHeight = height;

        // Aligned generously: LVGL's draw units may read the buffer with wide loads.
        var bytes = (nuint)width * (nuint)height * 4;
        _pixels = (uint*)NativeMemory.AlignedAlloc(bytes, 64);
        NativeMemory.Clear(_pixels, bytes);

        LvglNative.lv_canvas_set_buffer(Handle, _pixels, width, height, LvImageSource.ColorFormatArgb8888);
    }

    /// <summary>Width of the pixel buffer.</summary>
    public int BufferWidth { get; }

    /// <summary>Height of the pixel buffer.</summary>
    public int BufferHeight { get; }

    /// <summary>The pixels, row-major, <c>0xAARRGGBB</c>. Call <see cref="LvObject.Invalidate"/> after changing them.</summary>
    public Span<uint> Pixels => _pixels is not null
        ? new Span<uint>(_pixels, BufferWidth * BufferHeight)
        : throw new ObjectDisposedException(nameof(LvCanvas));

    /// <summary>Packs an opaque colour into the buffer's pixel format.</summary>
    public static uint Argb(LvColor color, byte alpha = 255) => ((uint)alpha << 24) | color.Rgb;

    /// <summary>Fills the whole buffer.</summary>
    public void Fill(uint argb) => Pixels.Fill(argb);

    /// <summary>Fills the whole buffer with an opaque colour.</summary>
    public void Fill(LvColor color) => Fill(Argb(color));

    /// <summary>Sets one pixel; coordinates outside the buffer are ignored.</summary>
    public void SetPixel(int x, int y, uint argb)
    {
        if ((uint)x < (uint)BufferWidth && (uint)y < (uint)BufferHeight) _pixels[y * BufferWidth + x] = argb;
    }

    /// <summary>Fills a rectangle, clipped to the buffer.</summary>
    public void FillRect(int x, int y, int width, int height, uint argb)
    {
        var x0 = Math.Max(x, 0);
        var y0 = Math.Max(y, 0);
        var x1 = Math.Min(x + width, BufferWidth);
        var y1 = Math.Min(y + height, BufferHeight);
        if (x0 >= x1 || y0 >= y1) return;

        var pixels = Pixels;
        for (var row = y0; row < y1; row++) pixels.Slice(row * BufferWidth + x0, x1 - x0).Fill(argb);
    }

    /// <summary>Fills a rectangle with an opaque colour.</summary>
    public void FillRect(int x, int y, int width, int height, LvColor color) => FillRect(x, y, width, height, Argb(color));

    /// <summary>Draws a one-pixel line (Bresenham), clipped to the buffer.</summary>
    public void DrawLine(int x0, int y0, int x1, int y1, uint argb)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;

        while (true)
        {
            SetPixel(x0, y0, argb);
            if (x0 == x1 && y0 == y1) return;

            var doubled = 2 * error;
            if (doubled >= dy) { error += dy; x0 += sx; }
            if (doubled <= dx) { error += dx; y0 += sy; }
        }
    }

    /// <summary>Draws a one-pixel line in an opaque colour.</summary>
    public void DrawLine(int x0, int y0, int x1, int y1, LvColor color) => DrawLine(x0, y0, x1, y1, Argb(color));

    /// <summary>Fills a circle centred on (<paramref name="cx"/>, <paramref name="cy"/>), clipped to the buffer.</summary>
    public void FillCircle(int cx, int cy, int radius, uint argb)
    {
        if (radius < 0) return;
        var pixels = Pixels;
        var r2 = radius * radius;

        for (var dy = -radius; dy <= radius; dy++)
        {
            var y = cy + dy;
            if ((uint)y >= (uint)BufferHeight) continue;

            var half = (int)Math.Sqrt(r2 - dy * dy);
            var x0 = Math.Max(cx - half, 0);
            var x1 = Math.Min(cx + half + 1, BufferWidth);
            if (x0 < x1) pixels.Slice(y * BufferWidth + x0, x1 - x0).Fill(argb);
        }
    }

    /// <summary>Fills a circle in an opaque colour.</summary>
    public void FillCircle(int cx, int cy, int radius, LvColor color) => FillCircle(cx, cy, radius, Argb(color));

    /// <inheritdoc />
    protected override void OnDeleted()
    {
        // LVGL sends the delete event before the canvas is destructed and never draws it again, and
        // its destructor only drops the image-cache entry by key - it does not read the pixels.
        if (_pixels is null) return;
        NativeMemory.AlignedFree(_pixels);
        _pixels = null;
    }
}
