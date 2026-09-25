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
/// </remarks>
public sealed class StaticRangePartitioner : IPartitioner
{
    /// <summary>The shared instance.</summary>
    public static StaticRangePartitioner Instance { get; } = new();

    /// <inheritdoc />
    public int GetPartitionCount(int length, int maxWorkers) => Math.Max(1, Math.Min(length, maxWorkers));

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
