// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics;
using AplNet.Core;
using AplNet.Simd;

namespace AplNet.Samples;

/// <summary>
/// A realistic per-pixel pipeline: convert an RGB image to luminance, then box-blur it. Each stage is a
/// different APL.Net shape - a ForRange body over pixels, then SIMD over rows - compared with the TPL
/// code a reader would write first.
/// </summary>
public static class ImageProcessingDemo
{
    private const int Width = 3840;
    private const int Height = 2160;

    public static void Run()
    {
        Console.WriteLine($"Image processing: {Width} x {Height} RGB -> luminance -> 3-row box blur");

        byte[] rgb = new byte[Width * Height * 3];
        new Random(1).NextBytes(rgb);
        float[] luma = new float[Width * Height];
        float[] blurred = new float[Width * Height];

        Measure("TPL  Parallel.For (pixels)", () =>
            Parallel.For(0, Width * Height, i => luma[i] = 0.299f * rgb[3 * i] + 0.587f * rgb[3 * i + 1] + 0.114f * rgb[3 * i + 2]));

        Measure("APL  Apl.ForRange (pixels)", () => Apl.ForRange(0, Width * Height, new LumaBody(rgb, luma)));

        Measure("TPL  Parallel.For (blur rows)", () => Parallel.For(1, Height - 1, y =>
        {
            for (int x = 0; x < Width; x++)
                blurred[y * Width + x] = (luma[(y - 1) * Width + x] + luma[y * Width + x] + luma[(y + 1) * Width + x]) * (1f / 3);
        }));

        Measure("APL  Apl.ForRange (blur rows)", () => Apl.ForRange(1, Height - 1, new BlurRowsBody(luma, blurred)));

        Console.WriteLine($"  mean luminance {SimdOps.ParallelSum(luma) / luma.Length:F2}, blurred {SimdOps.ParallelSum(blurred) / blurred.Length:F2}");
        Console.WriteLine();
    }

    private static void Measure(string name, Action action)
    {
        action();
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 5; i++)
            action();
        Console.WriteLine($"  {name,-38} {watch.Elapsed.TotalMilliseconds / 5,8:F2} ms");
    }

    private readonly struct LumaBody(byte[] rgb, float[] luma) : IRangeWorkBody
    {
        public void Invoke(int fromInclusive, int toExclusive)
        {
            ReadOnlySpan<byte> src = rgb.AsSpan(3 * fromInclusive, 3 * (toExclusive - fromInclusive));
            Span<float> dst = luma.AsSpan(fromInclusive, toExclusive - fromInclusive);
            for (int k = 0; k < dst.Length; k++)
                dst[k] = 0.299f * src[3 * k] + 0.587f * src[3 * k + 1] + 0.114f * src[3 * k + 2];
        }
    }

    /// <summary>
    /// One pass per row over spans: the check on output[x] disappears because x is bounded by its length,
    /// and the three input checks are always-taken branches. (Three SimdOps passes would vectorise each
    /// step but read and write the row three times - slower here, because the blur is limited by memory,
    /// not arithmetic.)
    /// </summary>
    private readonly struct BlurRowsBody(float[] luma, float[] blurred) : IRangeWorkBody
    {
        public void Invoke(int fromInclusive, int toExclusive)
        {
            for (int y = fromInclusive; y < toExclusive; y++)
            {
                ReadOnlySpan<float> above = luma.AsSpan((y - 1) * Width, Width);
                ReadOnlySpan<float> row = luma.AsSpan(y * Width, Width);
                ReadOnlySpan<float> below = luma.AsSpan((y + 1) * Width, Width);
                Span<float> output = blurred.AsSpan(y * Width, Width);
                for (int x = 0; x < output.Length; x++)
                    output[x] = (above[x] + row[x] + below[x]) * (1f / 3);
            }
        }
    }
}
