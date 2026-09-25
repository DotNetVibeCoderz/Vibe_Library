// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.CompilerServices;
using AplNet.Core;

namespace AplNet.Benchmarks;

/// <summary>The per-element work shared by every variant, so the loops differ only in how they dispatch.</summary>
public static class Workloads
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Trivial(double x) => x * 2 + 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Medium(double x) => Math.Sqrt(x) * Math.Sin(x);

    /// <summary>
    /// ~500 ns: raises a 4x4 matrix built from <paramref name="seed"/> to the 16th power by repeated
    /// multiplication (16 x 64 multiply-adds) and returns its trace.
    /// </summary>
    public static double Heavy(double seed)
    {
        Span<double> m = stackalloc double[16];
        Span<double> acc = stackalloc double[16];
        Span<double> tmp = stackalloc double[16];
        for (int k = 0; k < 16; k++)
        {
            m[k] = ((seed + k) % 7 - 3) * 0.1;
            acc[k] = k % 5 == 0 ? 1 : 0;
        }

        for (int step = 0; step < 16; step++)
        {
            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    double sum = 0;
                    for (int k = 0; k < 4; k++)
                        sum += acc[r * 4 + k] * m[k * 4 + c];
                    tmp[r * 4 + c] = sum;
                }
            }

            tmp.CopyTo(acc);
        }

        return acc[0] + acc[5] + acc[10] + acc[15];
    }

    /// <summary>A dependent chain of <paramref name="iterations"/> multiply-adds: cost proportional to the argument, not optimisable away.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static double Spin(int iterations, double x)
    {
        for (int i = 0; i < iterations; i++)
            x = x * 0.999999 + 1e-7;
        return x;
    }
}

public readonly struct TrivialBody(double[] source, double[] destination) : IWorkBody
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke(int i) => destination[i] = Workloads.Trivial(source[i]);
}

public readonly struct TrivialRangeBody(double[] source, double[] destination) : IRangeWorkBody
{
    public void Invoke(int fromInclusive, int toExclusive)
    {
        ReadOnlySpan<double> src = source.AsSpan(fromInclusive, toExclusive - fromInclusive);
        Span<double> dst = destination.AsSpan(fromInclusive, src.Length);
        for (int k = 0; k < src.Length; k++)
            dst[k] = Workloads.Trivial(src[k]);
    }
}

public readonly struct MediumBody(double[] source, double[] destination) : IWorkBody
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke(int i) => destination[i] = Workloads.Medium(source[i]);
}

public readonly struct HeavyBody(double[] source, double[] destination) : IWorkBody
{
    public void Invoke(int i) => destination[i] = Workloads.Heavy(source[i]);
}

public readonly struct SpinBody(int[] cost, double[] destination) : IWorkBody
{
    public void Invoke(int i) => destination[i] = Workloads.Spin(cost[i], i);
}

public readonly struct AxpyScalarBody(float[] data, float a, float b) : IWorkBody
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke(int i) => data[i] = data[i] * a + b;
}
