// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet.Core;
using AplNet.Core.Partitioners;

namespace AplNet.Tests;

internal static class TestSupport
{
    /// <summary>Every partitioner the executor accepts, including a custom one with more partitions than workers.</summary>
    public static IPartitioner[] Partitioners { get; } =
    [
        StaticRangePartitioner.Instance,
        new StripedPartitioner(1),
        new StripedPartitioner(7),
        new StripedPartitioner(4096),
        WorkStealingPartitioner.Instance,
        new WorkStealingPartitioner(1),
        new WorkStealingPartitioner(13),
        new OversplittingPartitioner(),
    ];

    public static AplOptions Options(int dop, IPartitioner? partitioner = null, CancellationToken token = default, int? minChunk = null) => new()
    {
        MaxDegreeOfParallelism = dop,
        Partitioner = partitioner,
        CancellationToken = token,
        MinChunkSize = minChunk,
    };

    /// <summary>Four static partitions per worker, claimed dynamically: exercises the claim loop with partitions &gt; workers.</summary>
    public sealed class OversplittingPartitioner : IPartitioner
    {
        public int GetPartitionCount(int length, int maxWorkers) => Math.Max(1, Math.Min(length, maxWorkers * 4));

        public bool TryGetRange(int length, int partitionCount, int partition, int index, out int fromInclusive, out int toExclusive) =>
            StaticRangePartitioner.Instance.TryGetRange(length, partitionCount, partition, index, out fromInclusive, out toExclusive);
    }

    /// <summary>Records the highest number of bodies running at once.</summary>
    public sealed class ConcurrencyProbe
    {
        private int _current;
        private int _peak;

        public int Peak => Volatile.Read(ref _peak);

        public void Enter()
        {
            int now = Interlocked.Increment(ref _current);
            int peak;
            while (now > (peak = Volatile.Read(ref _peak)) && Interlocked.CompareExchange(ref _peak, now, peak) != peak)
            {
            }
        }

        public void Exit() => Interlocked.Decrement(ref _current);
    }
}

/// <summary>Counts how many times each index was visited.</summary>
internal readonly struct CountingBody(int[] hits, int offset) : IWorkBody
{
    public void Invoke(int index) => Interlocked.Increment(ref hits[index - offset]);
}

internal readonly struct CountingRangeBody(int[] hits, int offset) : IRangeWorkBody
{
    public void Invoke(int fromInclusive, int toExclusive)
    {
        for (int i = fromInclusive; i < toExclusive; i++)
            Interlocked.Increment(ref hits[i - offset]);
    }
}

internal readonly struct AffineBody(double[] source, double[] destination) : IWorkBody
{
    public void Invoke(int i) => destination[i] = source[i] * 2 + 1;
}

internal readonly struct LongSumBody(long[] values) : IReduceBody<long>
{
    public long Accumulate(int fromInclusive, int toExclusive, long accumulator)
    {
        for (int i = fromInclusive; i < toExclusive; i++)
            accumulator += values[i];
        return accumulator;
    }

    public long Combine(long left, long right) => left + right;
}
