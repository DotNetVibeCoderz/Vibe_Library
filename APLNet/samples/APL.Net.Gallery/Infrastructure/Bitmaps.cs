// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AplNet.Gallery.Infrastructure;

public static class Bitmaps
{
    /// <summary>A bitmap from 32-bit BGRA pixels (0xAARRGGBB as a little-endian int).</summary>
    public static WriteableBitmap FromBgra(int[] pixels, int width, int height)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using ILockedFramebuffer frame = bitmap.Lock();
        for (int y = 0; y < height; y++)
            Marshal.Copy(pixels, y * width, frame.Address + y * frame.RowBytes, width);
        return bitmap;
    }

    public static int Rgb(int r, int g, int b) =>
        unchecked((int)0xFF000000) | (Math.Clamp(r, 0, 255) << 16) | (Math.Clamp(g, 0, 255) << 8) | Math.Clamp(b, 0, 255);
}
