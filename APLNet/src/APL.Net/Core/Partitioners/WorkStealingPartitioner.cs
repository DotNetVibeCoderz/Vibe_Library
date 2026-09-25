// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace AplNet.Core.Partitioners;

/// <summary>
/// Opt-in dynamic load balancing for loops whose iterations vary widely in cost.
/// </summary>
/// <remarks>
/// <para>
/// Each worker starts with the same contiguous range <see cref="StaticRangePartitioner"/> would give
/// it and consumes it from the front in grains of <see cref="MinGrainSize"/> iterations. A worker that
/// runs dry steals the back half of whatever remains of the fullest other range. Both ends of a range
/// live in one 64-bit word updated by compare-and-swap, so the owner and any number of thieves agree
/// on who runs what without locks. This is the index-range form of a Chase-Lev deque: the owner takes
/// from one end and thieves from the other, and because the work is a contiguous range rather than a
/// list of tasks, stealing half costs one CAS no matter how large the half is.
/// </para>
/// <para>
/// The price is one CAS per grain on the owner's side, which is why this is not the default: for
/// uniform, cheap iterations it is pure overhead. Choose it for skewed workloads (Mandelbrot rows,
/// Zipf-distributed item costs, sparse data), where it recovers most of what static partitioning
/// loses. Grains are also the unit at which cancellation and failures stop a worker.
/// </para>
/// </remarks>
public sealed class WorkStealingPartitioner : IPartitioner
{
    /// <summary>The shared instance, with an automatic grain size.</summary>
    public static WorkStealingPartitioner Instance { get; } = new();

    /// <summary>
    /// Creates a work-stealing partitioner. <paramref name="minGrainSize"/> is the number of
    /// iterations a worker takes from its own range at a time; 0 chooses
    /// <c>max(1, length / (workers * 1024))</c>, which keeps the CAS cost under a tenth of a percent
    /// of the iterations while leaving ~1000 grains per worker for thieves to take.
    /// </summary>
    public WorkStealingPartitioner(int minGrainSize = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minGrainSize);
        MinGrainSize = minGrainSize;
    }

    /// <summary>Iterations taken per grain, or 0 for automatic.</summary>
    public int MinGrainSize { get; }

    /// <summary>The grain size used for a loop of <paramref name="length"/> items over <paramref name="workers"/> workers.</summary>
    public int GetGrainSize(int length, int workers) =>
        MinGrainSize > 0 ? MinGrainSize : Math.Max(1, length / (Math.Max(1, workers) * 1024));

    /// <inheritdoc />
    public int GetPartitionCount(int length, int maxWorkers) => Math.Max(1, Math.Min(length, maxWorkers));

    /// <inheritdoc />
    /// <remarks>The initial ranges only; stealing moves work between them while the loop runs.</remarks>
    public bool TryGetRange(int length, int partitionCount, int partition, int index, out int fromInclusive, out int toExclusive) =>
        StaticRangePartitioner.Instance.TryGetRange(length, partitionCount, partition, index, out fromInclusive, out toExclusive);
}
