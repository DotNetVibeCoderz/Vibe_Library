// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace AplNet.Core.Partitioners;

/// <summary>
/// The default: one contiguous range per worker, all the same size to within one item.
/// </summary>
/// <remarks>
/// <para>
/// Computed once up front, with no interlocked chunk claiming while the loop runs - which is what
/// removes the per-chunk overhead of TPL's dynamic partitioner. The trade-off is load balance: if some
/// iterations cost far more than others, the worker that owns them finishes last and the rest wait.
/// Use <see cref="WorkStealingPartitioner"/> for such loops; do not make this one dynamic.
/// </para>
/// <para>
/// Sizes are balanced (the first <c>length % count</c> partitions get one extra item) rather than
/// <c>ceil(length / count)</c> each: with 9 items and 8 workers the ceiling split would give five
/// workers two items and leave three idle, while this gives one worker two and the rest one each.
/// </para>
/// <para>
/// <b>Straggler tolerance (opt-in).</b> With one partition per worker, a single worker that the OS
/// deschedules - another process, a busy core - holds up the whole loop. Passing
/// <c>partitionsPerWorker</c> greater than 1 splits the range into that many partitions per worker.
/// They are still fixed before the loop starts and claimed whole, one interlocked increment per
/// partition, so a late worker finds the leftovers already taken and the others absorb its share.
/// The price is <c>k</c> times as many claims and body calls; 2 to 4 is usually enough.
/// </para>
/// </remarks>
public sealed class StaticRangePartitioner : IPartitioner
{
    /// <summary>The shared instance: one partition per worker.</summary>
    public static StaticRangePartitioner Instance { get; } = new();

    /// <summary>
    /// Creates a static partitioner that splits each loop into <paramref name="partitionsPerWorker"/>
    /// equal partitions per worker. 1, the default, is the classic one-range-per-worker split.
    /// </summary>
    public StaticRangePartitioner(int partitionsPerWorker = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(partitionsPerWorker, 1);
        PartitionsPerWorker = partitionsPerWorker;
    }

    /// <summary>How many partitions each worker's share is split into.</summary>
    public int PartitionsPerWorker { get; }

    /// <inheritdoc />
    public int GetPartitionCount(int length, int maxWorkers) =>
        (int)Math.Max(1, Math.Min(length, (long)maxWorkers * PartitionsPerWorker));

    /// <inheritdoc />
    public bool TryGetRange(int length, int partitionCount, int partition, int index, out int fromInclusive, out int toExclusive)
    {
        if (index != 0 || (uint)partition >= (uint)partitionCount)
        {
            fromInclusive = toExclusive = 0;
            return false;
        }

        GetBalancedRange(length, partitionCount, partition, out fromInclusive, out toExclusive);
        return fromInclusive < toExclusive;
    }

    /// <summary>The <paramref name="partition"/>-th of <paramref name="partitionCount"/> near-equal slices of <c>[0, length)</c>.</summary>
    internal static void GetBalancedRange(int length, int partitionCount, int partition, out int fromInclusive, out int toExclusive)
    {
        int size = length / partitionCount;
        int remainder = length % partitionCount;
        fromInclusive = partition * size + Math.Min(partition, remainder);
        toExclusive = fromInclusive + size + (partition < remainder ? 1 : 0);
    }
}
