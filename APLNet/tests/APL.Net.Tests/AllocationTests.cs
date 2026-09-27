// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet.Core;
using AplNet.Simd;

namespace AplNet.Tests;

[CollectionDefinition(nameof(AllocationCollection), DisableParallelization = true)]
public sealed class AllocationCollection;

/// <summary>
/// FR7: the struct-body path allocates nothing in steady state. Measured process-wide (workers
/// allocate on their own threads) with nothing else running, over many calls so that one-off costs
/// such as growing the thread pool cannot hide a per-call allocation.
/// </summary>
[Collection(nameof(AllocationCollection))]
public class AllocationTests
{
    private const int Calls = 2000;

    // No more workers than cores. Asking for more makes the thread pool detect starvation and
    // inject threads, and creating a thread allocates - process-wide, where this probe looks. That
    // is the pool's cost, not the loop's; on a 2-core CI runner it showed up as ~40-140 B per call.
    private static readonly int s_dop = Math.Min(4, Environment.ProcessorCount);

    [Fact]
    public void StructBodyLoopsDoNotAllocatePerCall()
    {
        var data = new double[100_000];
        var result = new double[data.Length];
        var options = new AplOptions { MaxDegreeOfParallelism = s_dop };

        long perCall = MeasurePerCall(() => Apl.For(0, data.Length, new AffineBody(data, result), options));
        Assert.True(perCall < 8, $"struct-body For allocated {perCall} bytes per call");
    }

    [Fact]
    public void ReductionsAndSimdDoNotAllocatePerCall()
    {
        long[] values = Enumerable.Range(0, 100_000).Select(i => (long)i).ToArray();
        float[] floats = new float[200_000];
        var options = new AplOptions { MaxDegreeOfParallelism = s_dop, MinChunkSize = 10_000 };

        long reduce = MeasurePerCall(() => Apl.Reduce(0, values.Length, 0L, new LongSumBody(values), options));
        long sum = MeasurePerCall(() => SimdOps.ParallelSum(floats, options));
        long transform = MeasurePerCall(() => SimdOps.ParallelTransformInPlace(floats, new MultiplyAddOperator<float>(1, 0), options));
        Assert.True(reduce < 8 && sum < 8 && transform < 8, $"bytes per call: Reduce {reduce}, ParallelSum {sum}, ParallelTransformInPlace {transform}");
    }

    [Fact]
    public void WorkStealingDoesNotAllocatePerCall()
    {
        var hits = new int[50_000];
        var options = new AplOptions { MaxDegreeOfParallelism = s_dop, Partitioner = Core.Partitioners.WorkStealingPartitioner.Instance };
        Assert.True(MeasurePerCall(() => Apl.For(0, hits.Length, new CountingBody(hits, 0), options)) < 8);
    }

    [Fact]
    public void ParallelForAllocatesForComparison()
    {
        // Not a property of APL.Net - a sanity check that the measurement can see allocations at all,
        // so the zeros above mean something.
        var data = new double[100_000];
        var options = new ParallelOptions { MaxDegreeOfParallelism = s_dop };
        long perCall = MeasurePerCall(() => Parallel.For(0, data.Length, options, i => data[i] = i));
        Assert.True(perCall > 100, $"Parallel.For measured {perCall} bytes per call; the probe may be broken");
    }

    /// <summary>
    /// Bytes per call, as the lowest of several rounds. A per-call allocation shows up in every
    /// round; what does not is the runtime's own occasional allocation - most visibly the thread
    /// pool's work queue adding a 32 KB segment when queued items back up on a machine with few
    /// cores - which would otherwise be averaged into a loop that allocates nothing.
    /// </summary>
    private static long MeasurePerCall(Action action)
    {
        for (int i = 0; i < 200; i++)
            action();

        long best = long.MaxValue;
        for (int round = 0; round < 4; round++)
        {
            long before = GC.GetTotalAllocatedBytes(precise: true);
            for (int i = 0; i < Calls; i++)
                action();
            best = Math.Min(best, (GC.GetTotalAllocatedBytes(precise: true) - before) / Calls);
        }

        return best;
    }
}
