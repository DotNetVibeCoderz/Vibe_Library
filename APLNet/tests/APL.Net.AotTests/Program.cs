// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet;
using AplNet.Core;
using AplNet.Core.Partitioners;
using AplNet.Simd;
using AplNet.Unsafe;

// Every public entry point, checked against a plain loop, under whatever runtime this binary is:
// the JIT when run with "dotnet run", NativeAOT when published. Prints one line per check and exits
// non-zero if any failed.

int failures = 0;
void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    if (!ok)
        failures++;
}

// The CoreCLR host passes the deps files to the runtime; a NativeAOT executable has no host.
bool nativeAot = AppContext.GetData("APP_CONTEXT_DEPS_FILES") is null;
Console.WriteLine($"APL.Net checks - {(nativeAot ? "NativeAOT" : "JIT")}, {Environment.ProcessorCount} threads, {SimdCapabilities.Describe()}");

IPartitioner[] partitioners = [StaticRangePartitioner.Instance, new StripedPartitioner(64), new WorkStealingPartitioner(1)];
foreach (IPartitioner partitioner in partitioners)
{
    string p = partitioner.GetType().Name;
    foreach (int n in new[] { 0, 1, 3, 1000, 250_001 })
    {
        var options = new AplOptions { MaxDegreeOfParallelism = 8, Partitioner = partitioner };
        double[] src = Enumerable.Range(0, n).Select(i => i * 0.5).ToArray();
        double[] expected = src.Select(v => v * 2 + 1).ToArray();

        var a = new double[n];
        Apl.For(0, n, new Affine(src, a), options);
        Check($"For struct        {p,-24} n={n}", a.AsSpan().SequenceEqual(expected));

        var b = new double[n];
        Apl.For(0, n, i => b[i] = src[i] * 2 + 1, options);
        Check($"For delegate      {p,-24} n={n}", b.AsSpan().SequenceEqual(expected));

        var c = new double[n];
        Apl.ForRange(0, n, AplGen.AffineRange(src, c), options);
        Check($"ForRange AplGen   {p,-24} n={n}", c.AsSpan().SequenceEqual(expected));

        double[] d = (double[])src.Clone();
        Apl.ForEach(d, new InPlace(), options);
        Check($"ForEach ref       {p,-24} n={n}", d.AsSpan().SequenceEqual(expected));

        long[] values = Enumerable.Range(0, n).Select(i => (long)i * 7).ToArray();
        Check($"Reduce            {p,-24} n={n}", Apl.Reduce(0, n, 0L, new Sum(values), options) == values.Sum());
    }
}

foreach (int n in new[] { 0, 5, 33, 100_003 })
{
    var small = new AplOptions { MinChunkSize = 1000 };
    float[] f = Enumerable.Range(0, n).Select(i => (float)(i % 97) - 48).ToArray();
    double[] g = f.Select(v => (double)v).ToArray();
    int[] h = f.Select(v => (int)v).ToArray();
    long[] l = f.Select(v => (long)v).ToArray();

    float[] ft = (float[])f.Clone();
    SimdOps.ParallelTransformInPlace(ft, new MultiplyAddOperator<float>(3, 1), small);
    Check($"SIMD transform float  n={n}", ft.AsSpan().SequenceEqual(f.Select(v => v * 3 + 1).ToArray()));

    Check($"SIMD sum float        n={n}", SimdOps.ParallelSum(f, small) == f.Sum());
    Check($"SIMD sum double       n={n}", SimdOps.ParallelSum(g, small) == g.Sum());
    Check($"SIMD sum int          n={n}", SimdOps.ParallelSum(h, small) == h.Sum());
    Check($"SIMD sum long         n={n}", SimdOps.ParallelSum(l, small) == l.Sum());
    Check($"SIMD dot double       n={n}", SimdOps.ParallelDot(g, g, small) == g.Sum(v => v * v));
    if (n > 0)
    {
        Check($"SIMD min/max int      n={n}", SimdOps.ParallelMin(h, small) == h.Min() && SimdOps.ParallelMax(h, small) == h.Max());
    }
}

unsafe
{
    using var buffer = new NativeBuffer<double>(10_000);
    for (int i = 0; i < buffer.Length; i++)
        buffer[i] = i;
    UnsafeParallel.ForPtr(buffer.Pointer, buffer.Length, &Kernels.Double, 4);
    bool ok = true;
    for (int i = 0; i < buffer.Length; i++)
        ok &= buffer[i] == i * 2.0;
    Check("UnsafeParallel.ForPtr", ok);
}

using (var cts = new CancellationTokenSource())
{
    int ran = 0;
    bool canceled = false;
    try
    {
        Apl.For(0, 10_000_000, i => { if (Interlocked.Increment(ref ran) == 1000) cts.Cancel(); },
            new AplOptions { CancellationToken = cts.Token, CancellationCheckInterval = 64 });
    }
    catch (OperationCanceledException)
    {
        canceled = true;
    }

    Check("Cancellation mid-run", canceled && ran < 10_000_000);
}

try
{
    Apl.For(0, 1000, i => { if (i == 500) throw new InvalidOperationException("chunk"); }, new AplOptions { MaxDegreeOfParallelism = 4 });
    Check("Exception aggregation", false);
}
catch (AggregateException ex)
{
    Check("Exception aggregation", ex.InnerExceptions.Count == 1 && ex.InnerExceptions[0] is InvalidOperationException);
}

await Apl.ForEachAsync(Enumerable.Range(0, 10), async (i, ct) => await Task.Yield());
Check("ForEachAsync", true);

// 1.1: straggler-tolerant static partitions, 64-bit ranges, loop state, new SIMD operators.
{
    var hits = new int[100_003];
    Apl.For(0, hits.Length, i => hits[i]++, new AplOptions { MaxDegreeOfParallelism = 8, Partitioner = new StaticRangePartitioner(partitionsPerWorker: 4) });
    Check("Static partitioner, 4 partitions/worker", hits.All(h => h == 1));

    long covered = 0;
    Apl.ForRange(long.MinValue, long.MaxValue, (a, b) => Interlocked.Add(ref covered, unchecked(b - a)));
    Check("ForRange over the whole long range", covered == -1); // 2^64 - 1 iterations, wrapped

    var longHits = new int[1000];
    Apl.For(10_000_000_000L, 10_000_001_000L, new LongBody(longHits, 10_000_000_000L));
    Check("For long struct body", longHits.All(h => h == 1));

    AplLoopResult stopped = Apl.For(0, 1_000_000, (i, state) => { if (i == 10) state.Stop(); });
    Check("Loop state Stop", !stopped.IsCompleted && stopped.LowestBreakIteration is null);

    var ran = new int[100_000];
    AplLoopResult broken = Apl.ForWithState(0, ran.Length, new BreakAt(ran, 4321), new AplOptions { MaxDegreeOfParallelism = 8 });
    Check("Loop state Break", broken.LowestBreakIteration == 4321 && ran.AsSpan(0, 4321).IndexOf(0) < 0);

    double[] x = Enumerable.Range(1, 1000).Select(i => i * 0.01).ToArray();
    double[] y = new double[x.Length];
    SimdOps.Transform<double, LogOperator<double>>(x, y, default);
    SimdOps.TransformInPlace(y.AsSpan(), new ExpOperator<double>());
    Check("SIMD Exp(Log(x))", x.Zip(y).All(p => Math.Abs(p.First - p.Second) <= 1e-13 * p.First));

    float[] f = Enumerable.Range(0, 1000).Select(i => i * 0.1f).ToArray();
    float[] g = new float[f.Length];
    SimdOps.Transform(f, g, new FusedMultiplyAddOperator<float>(3, 0.5f));
    Check("SIMD fused multiply-add", f.Zip(g).All(p => MathF.FusedMultiplyAdd(p.First, 3, 0.5f) == p.Second));
}

// 1.2: persistent spinning workers, pinned, with NUMA topology detection (the native interop).
{
    Console.WriteLine($"Topology: {AplTopology.Describe()}");
    using var workers = new SpinningWorkers(Math.Max(1, Math.Min(3, Environment.ProcessorCount - 1)), affinity: WorkerAffinity.Core);
    Check("Spinning workers pinned where supported", workers.IsAffinityApplied == AplTopology.IsAffinitySupported);

    var options = new AplOptions { MaxDegreeOfParallelism = workers.ThreadCount + 1, Workers = workers };
    var data = new int[4096];
    for (int round = 0; round < 1000; round++)
        Apl.For(0, data.Length, i => data[i]++, options);
    Check("Spinning workers, 1000 small loops", data.All(d => d == 1000));
}

Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) FAILED.");
return failures == 0 ? 0 : 1;

internal readonly struct Affine(double[] src, double[] dst) : IWorkBody
{
    public void Invoke(int i) => dst[i] = src[i] * 2 + 1;
}

internal readonly struct LongBody(int[] hits, long offset) : ILongWorkBody
{
    public void Invoke(long index) => hits[index - offset]++;
}

internal readonly struct BreakAt(int[] ran, int at) : IStatefulWorkBody
{
    public void Invoke(int index, AplLoopState state)
    {
        ran[index] = 1;
        if (index >= at)
            state.Break();
    }
}

internal readonly struct InPlace : IWorkBody<double>
{
    public void Invoke(int index, ref double item) => item = item * 2 + 1;
}

internal readonly struct Sum(long[] values) : IReduceBody<long>
{
    public long Accumulate(int fromInclusive, int toExclusive, long accumulator)
    {
        foreach (long v in values.AsSpan(fromInclusive, toExclusive - fromInclusive))
            accumulator += v;
        return accumulator;
    }

    public long Combine(long left, long right) => left + right;
}

internal static unsafe class Kernels
{
    public static void Double(double* data, int i) => data[i] *= 2;

    [AplRangeBody]
    internal static void AffineRange(int fromInclusive, int toExclusive, double[] src, double[] dst)
    {
        for (int i = fromInclusive; i < toExclusive; i++)
            dst[i] = src[i] * 2 + 1;
    }
}
