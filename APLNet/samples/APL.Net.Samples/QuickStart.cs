// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics;
using AplNet.Core;
using AplNet.Core.Partitioners;
using AplNet.Simd;
using AplNet.Unsafe;

namespace AplNet.Samples;

/// <summary>The progression from docs/*/getting-started.md: delegate, struct, range, SIMD, unsafe.</summary>
public static unsafe class QuickStart
{
    private const int N = 2_000_000;

    public static void Run()
    {
        double[] src = Enumerable.Range(0, N).Select(i => i * 0.001).ToArray();
        double[] dst = new double[N];

        // 1. The TPL shape. A drop-in for Parallel.For with less overhead underneath.
        Time("Apl.For (lambda)", () => Apl.For(0, N, i => dst[i] = Math.Sqrt(src[i])));

        // 2. A struct body: no delegate call per element, no allocation per call.
        Time("Apl.For (struct)", () => Apl.For(0, N, new SqrtBody(src, dst)));

        // 3. A range body: one call per slice, spans without bounds checks.
        Time("Apl.ForRange (struct)", () => Apl.ForRange(0, N, new SqrtRangeBody(src, dst)));

        // 4. The generator writes the struct for you.
        Time("Apl.For (AplGen)", () => Apl.For(0, N, AplGen.Sqrt(src, dst)));

        // 5. SIMD, single-threaded and parallel.
        Time("SimdOps.Transform", () => SimdOps.Transform<double, SqrtOperator<double>>(src, dst, default));
        Time("SimdOps.ParallelTransform", () => SimdOps.ParallelTransform(src, dst, new SqrtOperator<double>()));
        Console.WriteLine($"  sum = {SimdOps.ParallelSum(dst):F3}");

        // 6. A reduction with a struct body.
        long evens = Apl.Reduce(0, N, 0L, new CountEvens());
        Console.WriteLine($"  even indices = {evens}");

        // 7. Uneven work: opt in to work stealing.
        var stealing = new AplOptions { Partitioner = WorkStealingPartitioner.Instance };
        Time("Apl.For (work stealing, skewed)", () => Apl.For(0, 2000, i => Thread.SpinWait(2000 * 2000 / (i + 1) / 20), stealing));

        // 8. The pointer tier, over aligned native memory.
        using var buffer = new NativeBuffer<double>(N);
        src.CopyTo(buffer.Span);
        Time("UnsafeParallel.ForRangePtr", () => UnsafeParallel.ForRangePtr(buffer.Pointer, N, &SqrtInPlace));

        // 9. Cancellation and failures behave as in TPL.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(5));
        try
        {
            Apl.For(0, int.MaxValue, _ => Thread.SpinWait(10), new AplOptions { CancellationToken = cts.Token });
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("  canceled after 5 ms, as asked");
        }

        try
        {
            Apl.For(0, 100, i => { if (i == 42) throw new InvalidOperationException("iteration 42"); });
        }
        catch (AggregateException ex)
        {
            Console.WriteLine($"  caught AggregateException: {ex.InnerExceptions[0].Message}");
        }

        Console.WriteLine();
    }

    private static void Time(string name, Action action)
    {
        for (int i = 0; i < 3; i++)
            action();
        var watch = Stopwatch.StartNew();
        const int runs = 10;
        for (int i = 0; i < runs; i++)
            action();
        Console.WriteLine($"  {name,-34} {watch.Elapsed.TotalMilliseconds / runs,8:F3} ms");
    }

    private static void SqrtInPlace(double* data, int from, int to)
    {
        for (int i = from; i < to; i++)
            data[i] = Math.Sqrt(data[i]);
    }

    private readonly struct SqrtBody(double[] src, double[] dst) : IWorkBody
    {
        public void Invoke(int i) => dst[i] = Math.Sqrt(src[i]);
    }

    private readonly struct SqrtRangeBody(double[] src, double[] dst) : IRangeWorkBody
    {
        public void Invoke(int fromInclusive, int toExclusive)
        {
            ReadOnlySpan<double> s = src.AsSpan(fromInclusive, toExclusive - fromInclusive);
            Span<double> d = dst.AsSpan(fromInclusive, s.Length);
            for (int k = 0; k < s.Length; k++)
                d[k] = Math.Sqrt(s[k]);
        }
    }

    private readonly struct CountEvens : IReduceBody<long>
    {
        public long Accumulate(int fromInclusive, int toExclusive, long accumulator) =>
            accumulator + (toExclusive - fromInclusive + (fromInclusive & 1 ^ 1)) / 2;

        public long Combine(long left, long right) => left + right;
    }

    [AplBody]
    internal static void Sqrt(int i, double[] src, double[] dst) => dst[i] = Math.Sqrt(src[i]);
}
