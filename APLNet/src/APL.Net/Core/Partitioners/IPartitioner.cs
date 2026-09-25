// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace AplNet.Core.Partitioners;

/// <summary>
/// Splits the index range <c>[0, length)</c> of a loop into partitions, once, before any work starts.
/// </summary>
/// <remarks>
/// <para>
/// A partition is a sequence of one or more non-overlapping sub-ranges. Workers claim whole
/// partitions (one interlocked increment per partition, never per iteration or per chunk) and run
/// every sub-range of the partition they claimed. Across all partitions the sub-ranges must cover
/// <c>[0, length)</c> exactly once.
/// </para>
/// <para>
/// Implementations must be thread-safe and deterministic: the executor may ask for the same range
/// more than once and from any thread. Both methods are called once per partition or sub-range, never
/// per iteration, so they need not be fast - but they must not allocate if the caller relies on the
/// zero-allocation guarantee.
/// </para>
/// </remarks>
public interface IPartitioner
{
    /// <summary>
    /// The number of partitions to create for <paramref name="length"/> items when at most
    /// <paramref name="maxWorkers"/> workers are available. Must be at least 1 when
    /// <paramref name="length"/> is positive.
    /// </summary>
    int GetPartitionCount(int length, int maxWorkers);

    /// <summary>
    /// Gets the <paramref name="index"/>-th sub-range of <paramref name="partition"/>, relative to the
    /// start of the loop. Returns <c>false</c> once the partition has no more sub-ranges.
    /// </summary>
    bool TryGetRange(int length, int partitionCount, int partition, int index, out int fromInclusive, out int toExclusive);
}
