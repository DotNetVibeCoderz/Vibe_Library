// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet.Core.Partitioners;
using AplNet.Diagnostics;

namespace AplNet.Core;

/// <summary>
/// The struct-invoker core that every APL.Net loop runs on. <see cref="Apl"/> is the friendlier entry
/// point; these are the same operations without the delegate overloads.
/// </summary>
/// <remarks>
/// <para>
/// <b>ExecutionContext does not flow.</b> Work is queued with
/// <see cref="ThreadPool.UnsafeQueueUserWorkItem(IThreadPoolWorkItem, bool)"/>, so a body running on a
/// pool worker does not see the caller's <see cref="AsyncLocal{T}"/> values, and does not inherit
/// culture or impersonation flowed through the execution context. Iterations the calling thread runs
/// itself do see them. <c>Parallel.For</c> flows the context to every iteration; if a body depends on
/// ambient state, pass it in explicitly (a field on the body struct) or use TPL.
/// </para>
/// <para>
/// Failures follow TPL: every exception thrown by the body is collected and rethrown as one
/// <see cref="AggregateException"/> once all running workers have stopped; cancellation through the
/// options' token surfaces as <see cref="OperationCanceledException"/>.
/// </para>
/// </remarks>
public static class ParallelExecutor
{
    /// <summary>Runs <paramref name="body"/> once for every index in <c>[fromInclusive, toExclusive)</c>.</summary>
    /// <exception cref="AggregateException">The body threw; contains every exception thrown.</exception>
    /// <exception cref="OperationCanceledException">The options' token was canceled.</exception>
    public static void For<TBody>(int fromInclusive, int toExclusive, TBody body, AplOptions? options = null)
        where TBody : struct, IWorkBody =>
        Run(fromInclusive, toExclusive, new ElementBody<TBody>(body), options, defaultMinChunk: 1);

    /// <summary>Runs <paramref name="body"/> over non-overlapping sub-ranges that together cover <c>[fromInclusive, toExclusive)</c>.</summary>
    /// <exception cref="AggregateException">The body threw; contains every exception thrown.</exception>
    /// <exception cref="OperationCanceledException">The options' token was canceled.</exception>
    public static void ForRange<TBody>(int fromInclusive, int toExclusive, TBody body, AplOptions? options = null)
        where TBody : struct, IRangeWorkBody =>
        Run(fromInclusive, toExclusive, body, options, defaultMinChunk: 1);

    /// <summary>
    /// Folds every index in <c>[fromInclusive, toExclusive)</c> into one value: each worker accumulates
    /// from <paramref name="identity"/>, and the partial results are combined in partition order.
    /// Returns <paramref name="identity"/> for an empty range.
    /// </summary>
    /// <exception cref="AggregateException">The body threw; contains every exception thrown.</exception>
    /// <exception cref="OperationCanceledException">The options' token was canceled.</exception>
    public static T Reduce<T, TBody>(int fromInclusive, int toExclusive, T identity, TBody body, AplOptions? options = null)
        where TBody : struct, IReduceBody<T> =>
        RunReduce(fromInclusive, toExclusive, identity, body, options, defaultMinChunk: 1);

    internal static void Run<TBody>(int fromInclusive, int toExclusive, in TBody body, AplOptions? options, int defaultMinChunk)
        where TBody : struct, IRangeWorkBody
    {
        options ??= AplOptions.Default;
        if (!TryPlan(fromInclusive, toExclusive, options, defaultMinChunk, out LoopPlan plan))
            return;

        if (plan.WorkerCount <= 1)
        {
            RunInline(body, plan);
            return;
        }

        RangeJob<TBody>.Run(body, plan);
    }

    internal static T RunReduce<T, TBody>(int fromInclusive, int toExclusive, T identity, in TBody body, AplOptions? options, int defaultMinChunk)
        where TBody : struct, IReduceBody<T>
    {
        options ??= AplOptions.Default;
        if (!TryPlan(fromInclusive, toExclusive, options, defaultMinChunk, out LoopPlan plan))
            return identity;

        if (plan.WorkerCount <= 1)
        {
            ReduceInline<T, TBody> inline = new(body, identity);
            RunInline(ref inline, plan);
            return inline.Accumulator;
        }

        return ReduceJob<T, TBody>.Run(body, identity, plan);
    }

    /// <summary>Decides how a loop will run. False when there is nothing to do.</summary>
    internal static bool TryPlan(int fromInclusive, int toExclusive, AplOptions options, int defaultMinChunk, out LoopPlan plan)
    {
        long count = (long)toExclusive - fromInclusive;
        if (count <= 0)
        {
            plan = default;
            return false;
        }

        CancellationToken token = options.CancellationToken;
        token.ThrowIfCancellationRequested();

        if (count > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(toExclusive), "A loop may cover at most Int32.MaxValue iterations.");

        int length = (int)count;
        int dop = options.GetEffectiveDegreeOfParallelism();
        int minChunk = options.MinChunkSize ?? defaultMinChunk;
        int maxWorkers = (int)Math.Min(dop, ((long)length + minChunk - 1) / minChunk);
        int blockSize = token.CanBeCanceled ? options.CancellationCheckInterval : int.MaxValue;
        IPartitioner partitioner = options.Partitioner ?? StaticRangePartitioner.Instance;

        int partitionCount;
        int workers;
        int grain = 0;
        bool stealing = partitioner is WorkStealingPartitioner;

        if (maxWorkers <= 1)
        {
            partitionCount = workers = 1;
            stealing = false;
        }
        else if (stealing)
        {
            // Stealing needs exactly one initial range per worker.
            partitionCount = workers = Math.Min(maxWorkers, length);
            grain = ((WorkStealingPartitioner)partitioner).GetGrainSize(length, workers);
        }
        else
        {
            partitionCount = partitioner.GetPartitionCount(length, maxWorkers);
            if (partitionCount < 1)
                throw new InvalidOperationException($"{partitioner.GetType().Name}.GetPartitionCount returned {partitionCount}; it must be at least 1.");
            workers = Math.Min(maxWorkers, partitionCount);
        }

        plan = new LoopPlan
        {
            From = fromInclusive,
            Length = length,
            WorkerCount = workers,
            PartitionCount = partitionCount,
            Partitioner = partitioner,
            BlockSize = blockSize,
            Token = token,
            Stealing = stealing,
            Grain = grain,
        };
        return true;
    }

    /// <summary>A loop too small to split runs on the caller, with the same exception and cancellation behaviour as a parallel one.</summary>
    private static void RunInline<TBody>(TBody body, in LoopPlan plan)
        where TBody : struct, IRangeWorkBody =>
        RunInline(ref body, plan);

    private static void RunInline<TBody>(ref TBody body, in LoopPlan plan)
        where TBody : struct, IRangeWorkBody
    {
        if (AplEventSource.Log.IsEnabled())
            AplEventSource.Log.OnLoop(inline: true);

        int from = plan.From;
        int to = from + plan.Length;
        int block = plan.BlockSize;
        CancellationToken token = plan.Token;
        try
        {
            if (block == int.MaxValue)
            {
                body.Invoke(from, to);
                return;
            }

            while (from < to)
            {
                token.ThrowIfCancellationRequested();
                int end = to - from > block ? from + block : to;
                body.Invoke(from, end);
                from = end;
            }
        }
        catch (OperationCanceledException oce) when (token.IsCancellationRequested && oce.CancellationToken == token)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AggregateException(ex);
        }
    }

    /// <summary>Adapts a reduction to the range-body shape so the inline path can share <see cref="RunInline{TBody}(ref TBody, in LoopPlan)"/>.</summary>
    private struct ReduceInline<T, TBody>(TBody body, T identity) : IRangeWorkBody
        where TBody : struct, IReduceBody<T>
    {
        private TBody _body = body;

        public T Accumulator = identity;

        public void Invoke(int fromInclusive, int toExclusive) =>
            Accumulator = _body.Accumulate(fromInclusive, toExclusive, Accumulator);
    }
}
