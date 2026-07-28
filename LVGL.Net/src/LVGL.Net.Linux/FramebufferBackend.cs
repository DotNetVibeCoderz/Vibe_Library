using System.Runtime.CompilerServices;
using Lvgl.Backends;
using Lvgl.Drawing;

namespace Lvgl.Linux;

/// <summary>
/// Draws LVGL straight into a Linux framebuffer device (<c>/dev/fb0</c>), with no window system.
/// </summary>
/// <remarks>
/// <para>
/// This is the backend for a Raspberry Pi driving a panel directly - a kiosk or instrument display
/// with no desktop running. It memory-maps the framebuffer and copies each flushed region into it,
/// so a redraw costs one <c>memcpy</c> per scan line and nothing else.
/// </para>
/// <para>
/// <b>Permissions.</b> The framebuffer is owned by the <c>video</c> group. Either add the user
/// (<c>sudo usermod -aG video $USER</c>, then log out and back in) or run as root.
/// </para>
/// <para>
/// <b>Console cursor.</b> The kernel keeps blinking the text cursor over the image; disable it with
/// <c>sudo sh -c 'echo 0 &gt; /sys/class/graphics/fbcon/cursor_blink'</c>.
/// </para>
/// </remarks>
public sealed unsafe class FramebufferBackend : ILvglBackend
{
    private readonly IPointerSource? _pointerSource;
    private int _fd = -1;
    private nint _map;
    private nuint _mapLength;
    private byte* _pixels;
    private bool _disposed;

    /// <summary>Opens the framebuffer device.</summary>
    /// <param name="devicePath">Framebuffer device, usually <c>/dev/fb0</c>.</param>
    /// <param name="pointerSource">
    /// Optional touch or mouse source; <see cref="EvdevPointer"/> is the usual choice. Without one
    /// the UI renders but cannot be interacted with.
    /// </param>
    public FramebufferBackend(string devicePath = "/dev/fb0", IPointerSource? pointerSource = null)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("The framebuffer backend requires Linux.");
        }

        _pointerSource = pointerSource;

        _fd = LibC.Open(devicePath, LibC.O_RDWR);
        if (_fd < 0)
        {
            throw new LvglException(
                $"Could not open {devicePath}: {LibC.LastError()}. " +
                "Add your user to the 'video' group (sudo usermod -aG video $USER) and log in again.");
        }

        try
        {
            ReadScreenInfo();

            _mapLength = (nuint)(Stride * (long)Height);
            _map = LibC.Mmap(0, _mapLength, LibC.PROT_READ | LibC.PROT_WRITE, LibC.MAP_SHARED, _fd, 0);
            if (_map == LibC.MAP_FAILED || _map == 0)
            {
                throw new LvglException($"Could not map {devicePath}: {LibC.LastError()}");
            }

            _pixels = (byte*)_map;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Bytes per row of the mapped framebuffer, which may exceed <c>Width * BytesPerPixel</c>.</summary>
    public int Stride { get; private set; }

    /// <summary>Colour depth reported by the device: 32 and 16 bits per pixel are supported.</summary>
    public int BitsPerPixel { get; private set; }

    /// <summary>
    /// True when the device orders its 32-bit pixels as RGBA rather than the BGRA that LVGL
    /// produces, in which case the red and blue channels are swapped during the blit.
    /// </summary>
    public bool SwapRedBlue { get; private set; }

    public LvPointerState Pointer => _pointerSource?.Pointer ?? default;

    private void ReadScreenInfo()
    {
        // fb_var_screeninfo and fb_fix_screeninfo are read as raw bytes: both contain a
        // `unsigned long`, so their layout differs between 32-bit and 64-bit userland and a
        // declared struct would be wrong on one of the two.
        var variable = stackalloc byte[160];
        if (LibC.Ioctl(_fd, LibC.FBIOGET_VSCREENINFO, variable) != 0)
        {
            throw new LvglException($"FBIOGET_VSCREENINFO failed: {LibC.LastError()}");
        }

        Width = (int)*(uint*)(variable + 0);
        Height = (int)*(uint*)(variable + 4);
        BitsPerPixel = (int)*(uint*)(variable + 24);

        // fb_bitfield red starts at 32 and blue at 56; each is {offset, length, msb_right}.
        var redOffset = *(uint*)(variable + 32);
        var blueOffset = *(uint*)(variable + 56);
        SwapRedBlue = BitsPerPixel == 32 && redOffset < blueOffset;

        var fixedInfo = stackalloc byte[120];
        if (LibC.Ioctl(_fd, LibC.FBIOGET_FSCREENINFO, fixedInfo) != 0)
        {
            throw new LvglException($"FBIOGET_FSCREENINFO failed: {LibC.LastError()}");
        }

        // line_length follows id[16] + smem_start (pointer-sized) + five u32s + three u16s + pad.
        var lineLengthOffset = nint.Size == 8 ? 48 : 44;
        Stride = (int)*(uint*)(fixedInfo + lineLengthOffset);

        if (Width <= 0 || Height <= 0 || Stride <= 0)
        {
            throw new LvglException($"The framebuffer reported an unusable geometry: {Width}x{Height}, stride {Stride}.");
        }

        if (BitsPerPixel is not (32 or 16))
        {
            throw new LvglException(
                $"{BitsPerPixel}-bit framebuffers are not supported; set a 32- or 16-bit mode " +
                "(on a Pi: add framebuffer_depth=32 to /boot/firmware/config.txt).");
        }
    }

    public void Blit(in LvArea area, nint pixels, bool isLastFlush)
    {
        if (_pixels is null) return;

        var clipped = area.Clip(new LvArea(0, 0, Width - 1, Height - 1));
        if (clipped.IsEmpty) return;

        var source = (byte*)pixels;
        var sourceStride = area.Width * 4;
        var leadingPixels = clipped.X1 - area.X1;

        for (var y = clipped.Y1; y <= clipped.Y2; y++)
        {
            var sourceRow = (uint*)(source + (y - area.Y1) * sourceStride) + leadingPixels;
            var destinationRow = _pixels + y * (long)Stride;

            switch (BitsPerPixel)
            {
                case 32 when !SwapRedBlue:
                    // Identical layout - one straight copy, which is the fast path this backend
                    // is designed around.
                    Unsafe.CopyBlock(destinationRow + clipped.X1 * 4, sourceRow, (uint)(clipped.Width * 4));
                    break;

                case 32:
                    CopySwapped(sourceRow, (uint*)(destinationRow + clipped.X1 * 4), clipped.Width);
                    break;

                default:
                    CopyAsRgb565(sourceRow, (ushort*)(destinationRow + clipped.X1 * 2), clipped.Width);
                    break;
            }
        }
    }

    private static void CopySwapped(uint* source, uint* destination, int count)
    {
        for (var x = 0; x < count; x++)
        {
            var pixel = source[x];
            destination[x] = (pixel & 0xFF00FF00u) | ((pixel & 0x00FF0000u) >> 16) | ((pixel & 0x000000FFu) << 16);
        }
    }

    private static void CopyAsRgb565(uint* source, ushort* destination, int count)
    {
        for (var x = 0; x < count; x++)
        {
            var pixel = source[x];
            var r = (pixel >> 19) & 0x1F;
            var g = (pixel >> 10) & 0x3F;
            var b = (pixel >> 3) & 0x1F;
            destination[x] = (ushort)((r << 11) | (g << 5) | b);
        }
    }

    public bool PumpEvents()
    {
        _pointerSource?.Poll();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_map != 0 && _map != LibC.MAP_FAILED)
        {
            LibC.Munmap(_map, _mapLength);
            _map = 0;
            _pixels = null;
        }

        if (_fd >= 0)
        {
            LibC.Close(_fd);
            _fd = -1;
        }

        _pointerSource?.Dispose();
    }
}

/// <summary>A source of pointer state that must be polled once per frame.</summary>
public interface IPointerSource : IDisposable
{
    /// <summary>Latest known pointer state.</summary>
    LvPointerState Pointer { get; }

    /// <summary>Drains pending device events.</summary>
    void Poll();
}
