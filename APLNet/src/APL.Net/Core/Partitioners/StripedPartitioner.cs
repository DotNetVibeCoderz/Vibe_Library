// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace AplNet.Core.Partitioners;

/// <summary>
/// Deals fixed-size stripes round-robin: worker <c>w</c> of <c>W</c> runs stripes
/// <c>w, w + W, w + 2W, ...</c>.
/// </summary>
/// <remarks>
/// <para>
/// Still static - every assignment is known before the loop starts - but interleaved. That helps
/// when cost varies smoothly with the index (a triangular matrix, an image whose top half is empty):
/// each worker gets a slice of every region instead of one worker getting all of the expensive one.
/// It also keeps all workers moving through the same neighbourhood of memory at the same time, which
/// suits access patterns where nearby iterations share data.
/// </para>
/// <para>
/// Pick a stripe large enough to amortise the per-stripe call (thousands of cheap iterations, or a
/// handful of expensive ones) and, for arrays, a multiple of 16 so stripes start on cache-line
/// boundaries for 4-byte elements.
/// </para>
/// </remarks>
public sealed class StripedPartitioner : IPartitioner
{
    /// <summary>Creates a partitioner dealing stripes of <paramref name="stripeSize"/> iterations.</summary>
    public StripedPartitioner(int stripeSize = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(stripeSize, 1);
        StripeSize = stripeSize;
    }

    /// <summary>The number of consecutive iterations in one stripe.</summary>
    public int StripeSize { get; }

    /// <inheritdoc />
    public int GetPartitionCount(int length, int maxWorkers)
    {
        long stripes = ((long)length + StripeSize - 1) / StripeSize;
        return (int)Math.Max(1, Math.Min(stripes, maxWorkers));
    }

    /// <inheritdoc />
    public bool TryGetRange(int length, int partitionCount, int partition, int index, out int fromInclusive, out int toExclusive)
    {
        long stripe = partition + (long)index * partitionCount;
        long start = stripe * StripeSize;
        if ((uint)partition >= (uint)partitionCount || index < 0 || start >= length)
        {
            fromInclusive = toExclusive = 0;
            return false;
        }

        fromInclusive = (int)start;
        toExclusive = (int)Math.Min(start + StripeSize, length);
        return true;
    }
}
