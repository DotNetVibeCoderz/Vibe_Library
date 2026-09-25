// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet.Core;
using AplNet.Core.Partitioners;

namespace AplNet;

/// <summary>
/// Per-call settings for APL.Net loops: the equivalent of <see cref="ParallelOptions"/>.
/// </summary>
/// <remarks>
/// An instance is immutable once built and safe to share between threads and calls, so hot paths
/// should create one up front and reuse it rather than allocating a new one per call.
/// </remarks>
public sealed class AplOptions
{
    /// <summary>The options used when <c>null</c> is passed: every setting at its default.</summary>
    public static AplOptions Default { get; } = new();

    /// <summary>
    /// The most workers a loop may use, counting the calling thread. <c>null</c> or <c>-1</c> means
    /// "use <see cref="ParallelismProvider"/>", which defaults to <see cref="Environment.ProcessorCount"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less than -1.</exception>
    public int? MaxDegreeOfParallelism
    {
        get;
        init
        {
            if (value is 0 or < -1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxDegreeOfParallelism must be positive, or -1 for the default.");
            field = value == -1 ? null : value;
        }
    }

    /// <summary>
    /// Observed before the loop starts and between blocks of <see cref="CancellationCheckInterval"/>
    /// iterations. A canceled loop throws <see cref="OperationCanceledException"/>, as TPL does.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// How the index range is split into partitions. <c>null</c> means
    /// <see cref="StaticRangePartitioner.Instance"/>. See <see cref="StripedPartitioner"/> and
    /// <see cref="WorkStealingPartitioner"/> for the alternatives.
    /// </summary>
    public IPartitioner? Partitioner { get; init; }

    /// <summary>
    /// Supplies the default degree of parallelism. Inject a fixed provider to make scheduling
    /// deterministic in tests, independent of the machine's core count.
    /// </summary>
    public IParallelismProvider? ParallelismProvider { get; init; }

    /// <summary>
    /// The fewest iterations worth handing to one worker. A loop over <c>n</c> items uses at most
    /// <c>ceil(n / MinChunkSize)</c> workers, so tiny loops are not split into slices smaller than the
    /// cost of waking a thread. <c>null</c> uses the API's own default: 1 for the loop methods (the
    /// body's cost is unknown), 32 768 elements for the SIMD kernels.
    /// </summary>
    public int? MinChunkSize
    {
        get;
        init
        {
            if (value is <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "MinChunkSize must be positive.");
            field = value;
        }
    }

    /// <summary>
    /// When <see cref="CancellationToken"/> can be canceled, the loop body is invoked in blocks of at
    /// most this many iterations and the token is checked between blocks. A range body therefore sees
    /// ranges no longer than this. Ignored when the token cannot be canceled: the body then receives
    /// whole partitions and pays nothing for cancellation support. Default 4096.
    /// </summary>
    public int CancellationCheckInterval
    {
        get;
        init
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "CancellationCheckInterval must be positive.");
            field = value;
        }
    } = 4096;

    /// <summary>The degree of parallelism this call will use before range-size limits are applied.</summary>
    public int GetEffectiveDegreeOfParallelism()
    {
        int dop = MaxDegreeOfParallelism ?? (ParallelismProvider ?? DefaultParallelismProvider.Instance).DegreeOfParallelism;
        return dop < 1 ? 1 : dop;
    }
}
