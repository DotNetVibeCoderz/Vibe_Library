using System.Runtime.InteropServices;
using Lvgl.Interop;

namespace Lvgl.Drawing;

/// <summary>
/// Pixels or a file for an <see cref="Widgets.LvImage"/> to show.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds exist. A <b>file</b> source is just a path: LVGL opens and decodes it (PNG and BMP are
/// compiled in) and caches the decoded pixels. A <b>pixel</b> source wraps a copy of caller-supplied
/// ARGB pixels in unmanaged memory, for images produced at run time - a camera frame, a rendered
/// QR code, a thumbnail decoded by the application.
/// </para>
/// <para>
/// <b>Lifetime.</b> A pixel source must outlive every image showing it, because LVGL draws straight
/// from its memory - the same rule as <see cref="Styling.LvStyle"/>. Disposing also evicts it from
/// LVGL's image cache, so the address cannot be mistaken for a later image.
/// </para>
/// </remarks>
public sealed unsafe class LvImageSource : IDisposable
{
    // lv_color_format_t values, verified against the native build by the integration tests.
    internal const uint ColorFormatArgb8888 = 0x10;

    private nint _descriptor;
    private void* _pixels;

    private LvImageSource(string path)
    {
        FilePath = path;
        Width = 0;
        Height = 0;
    }

    private LvImageSource(nint descriptor, void* pixels, int width, int height)
    {
        _descriptor = descriptor;
        _pixels = pixels;
        Width = width;
        Height = height;
    }

    /// <summary>The file this source refers to, or null for a pixel source.</summary>
    public string? FilePath { get; }

    /// <summary>True for a file source.</summary>
    public bool IsFile => FilePath is not null;

    /// <summary>Pixel width; 0 for a file source, whose size is known only once LVGL decodes it.</summary>
    public int Width { get; }

    /// <summary>Pixel height; 0 for a file source.</summary>
    public int Height { get; }

    /// <summary>Native <c>lv_image_dsc_t*</c> of a pixel source.</summary>
    internal nint Descriptor => _descriptor != 0 || IsFile
        ? _descriptor
        : throw new ObjectDisposedException(nameof(LvImageSource));

    /// <summary>
    /// Refers to an image file. PNG and BMP are decoded by the native library.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static LvImageSource FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Image file not found.", fullPath);
        return new LvImageSource(fullPath);
    }

    /// <summary>
    /// Copies 32-bit pixels into an image. Each value is <c>0xAARRGGBB</c>; alpha is honoured.
    /// </summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="argb">Row-major pixels, <paramref name="width"/> × <paramref name="height"/> of them.</param>
    public static LvImageSource FromArgb(int width, int height, ReadOnlySpan<uint> argb)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 0xFFFF);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, 0xFFFF);

        var count = (long)width * height;
        if (argb.Length < count)
        {
            throw new ArgumentException($"Expected {count} pixels for {width}x{height}, got {argb.Length}.", nameof(argb));
        }

        var stride = width * 4;
        if (stride > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(width), "The image is too wide for LVGL's 16-bit stride.");

        LvglRuntime.Initialize();

        var bytes = (nuint)(count * 4);
        var pixels = NativeMemory.Alloc(bytes);

        // 0xAARRGGBB as a little-endian uint is B,G,R,A in memory - exactly LVGL's ARGB8888 layout,
        // so this is a straight copy. A big-endian host would need a swizzle here.
        argb[..(int)count].CopyTo(new Span<uint>(pixels, (int)count));

        var descriptor = LvglNative.lvn_image_dsc_create(
            (uint)width, (uint)height, ColorFormatArgb8888, (uint)stride, pixels, (uint)bytes);

        if (descriptor == 0)
        {
            NativeMemory.Free(pixels);
            throw new LvglException("Could not create an LVGL image descriptor.");
        }

        return new LvImageSource(descriptor, pixels, width, height);
    }

    /// <summary>A solid-colour image - handy for placeholders and swatches.</summary>
    public static LvImageSource Solid(int width, int height, LvColor color, byte alpha = 255)
    {
        var pixels = new uint[width * height];
        Array.Fill(pixels, ((uint)alpha << 24) | color.Rgb);
        return FromArgb(width, height, pixels);
    }

    /// <summary>
    /// Releases the pixels. Only call this once no image shows the source any more; must run on
    /// the LVGL thread because it evicts the image from LVGL's cache.
    /// </summary>
    public void Dispose()
    {
        if (_descriptor != 0)
        {
            LvglRuntime.EnsureUiThread();
            LvglNative.lvn_image_dsc_delete(_descriptor);
            _descriptor = 0;
        }

        if (_pixels is not null)
        {
            NativeMemory.Free(_pixels);
            _pixels = null;
        }
    }
}
