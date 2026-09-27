// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.CompilerServices;
using AplNet.Core.Partitioners;
using AplNet.Diagnostics;

namespace AplNet.Core;

/// <summary>Everything decided about a loop before it starts.</summary>
internal readonly struct LoopPlan
{
    public required int From { get; init; }
    public required int Length { get; init; }
    public required int WorkerCount { get; init; }
    public required int PartitionCount { get; init; }
    public required IPartitioner Partitioner { get; init; }
    public required int BlockSize { get; init; }
    public required CancellationToken Token { get; init; }
    public required bool Stealing { get; init; }
    public required int Grain { get; init; }
}

/// <summary>
/// One parallel loop in flight: the scheduling state shared by the caller and the pool workers.
/// </summary>
/// <remarks>
/// <para>
/// Instances are pooled per body type, so a steady stream of loops allocates nothing: no closure, no
/// <see cref="CountdownEvent"/>, no <see cref="Task"/>. The job itself is the
/// <see cref="IThreadPoolWorkItem"/> - queued <c>WorkerCount - 1</c> times through
/// <see cref="ThreadPool.UnsafeQueueUserWorkItem(IThreadPoolWorkItem, bool)"/>, which neither
/// allocates nor captures the <see cref="ExecutionContext"/>.
/// </para>
/// <para>
/// The calling thread is one of the workers. It never waits for a queued work item to be picked up,
/// only for partitions that are actually running: if the pool is slow to respond (busy, or this loop
/// is nested inside another) the caller simply claims the partitions itself. That is why a nested loop
/// cannot deadlock the pool, and why a work item may run after its loop has already returned.
/// </para>
/// <para>
/// Late items are handled by a <em>gate</em> (<see cref="_gate"/>): an open flag plus a count of pool
/// workers inside. The caller opens it once the run's fields are set, and when every partition is
/// done it closes it and waits for the workers still inside to leave - they are only looking for work
/// that no longer exists, so this is a few spins. From then on the job is idle and goes straight back
/// to its pool. A late item that arrives while the gate is closed returns without touching anything;
/// one that arrives after the job has been reused for another loop of the same body type simply
/// helps with that loop. So a job never waits in limbo for its stale items, and a steady stream of
/// calls reuses one job even when the machine has fewer cores than the loop has workers.
/// (An earlier design reference-counted the items and returned the job when the last one ran; on a
/// two-core machine the items lagged behind the calls and every few calls needed a new job.)
/// </para>
/// </remarks>
internal abstract class ParallelJob : IThreadPoolWorkItem
{
    private const int SlotStride = 8; // longs per work-stealing slot: one 64-byte cache line each
    private const int NoStop = 0;
    private const int StopFaulted = 1;
    private const int StopCanceled = 2;
    private const int GateOpen = 1 << 30;
    private const int GateCountMask = GateOpen - 1;

    private readonly ManualResetEventSlim _done = new(initialState: false);
    private readonly Lock _exceptionLock = new();

    private int _from;
    private int _length;
    private int _partitionCount;
    private int _workerCount;
    private int _blockSize;
    private int _grain;
    private bool _stealing;
    private IPartitioner _partitioner = StaticRangePartitioner.Instance;
    private CancellationToken _token;
    private long[] _slots = [];

    private int _next;       // static: next partition to claim; stealing: next worker slot
    private int _remaining;  // static: partitions not finished; stealing: iterations not finished
    private int _stop;
    private int _gate;       // GateOpen while a run accepts pool workers, plus the number inside
    private List<Exception>? _exceptions;

    /// <summary>Accumulator slots a reduction needs: one per partition, or one per worker when stealing.</summary>
    protected int SlotCount => _stealing ? _workerCount : _partitionCount;

    /// <summary>Runs the body over <c>[fromInclusive, toExclusive)</c>. <paramref name="slot"/> is private to the running worker.</summary>
    protected abstract void InvokeRange(int fromInclusive, int toExclusive, int slot);

    /// <summary>Called on the caller's thread before any work is queued.</summary>
    protected virtual void Prepare(int slotCount)
    {
    }

    /// <summary>Drops references held for the last loop and offers the instance back to its pool.</summary>
    protected abstract void ReturnToPool();

    /// <summary>Starts the loop, takes part in it, and returns once every iteration has finished or been abandoned.</summary>
    protected void Execute(in LoopPlan plan)
    {
        _from = plan.From;
        _length = plan.Length;
        _partitionCount = plan.PartitionCount;
        _workerCount = plan.WorkerCount;
        _blockSize = plan.BlockSize;
        _grain = plan.Grain;
        _stealing = plan.Stealing;
        _partitioner = plan.Partitioner;
        _token = plan.Token;
        _stop = NoStop;
        _exceptions = null;
        _next = 0;

        if (_stealing)
        {
            InitializeSlots();
            _remaining = _length;
        }
        else
        {
            _remaining = _partitionCount;
        }

        Prepare(SlotCount);

        int queued = _workerCount - 1;
        _done.Reset();

        // Everything above is published by this write: a pool worker reads the run's fields only
        // after it has seen the gate open.
        Volatile.Write(ref _gate, GateOpen);

        if (AplEventSource.Log.IsEnabled())
            AplEventSource.Log.OnLoop(inline: false);

        for (int i = 0; i < queued; i++)
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);

        Work();

        if (Volatile.Read(ref _remaining) != 0)
            _done.Wait();
    }

    /// <summary>
    /// Closes the gate and waits for the pool workers still inside to leave. After this, no other
    /// thread touches the run's state, so the outcome can be read and the job reused.
    /// </summary>
    protected void CloseGate()
    {
        Interlocked.And(ref _gate, ~GateOpen);
        if ((Volatile.Read(ref _gate) & GateCountMask) == 0)
            return;

        SpinWait spinner = default;
        while ((Volatile.Read(ref _gate) & GateCountMask) != 0)
            spinner.SpinOnce();
    }

    /// <summary>What the loop ended with. Read by the caller after <see cref="CloseGate"/>.</summary>
    protected void GetOutcome(out List<Exception>? exceptions, out bool canceled, out CancellationToken token)
    {
        exceptions = _exceptions;
        canceled = Volatile.Read(ref _stop) == StopCanceled;
        token = _token;
    }

    /// <summary>Throws what TPL would: every captured exception wrapped in one <see cref="AggregateException"/>, else <see cref="OperationCanceledException"/>.</summary>
    protected static void ThrowIfFailed(List<Exception>? exceptions, bool canceled, CancellationToken token)
    {
        if (exceptions is not null)
            throw new AggregateException(exceptions);
        if (canceled)
            throw new OperationCanceledException(token);
    }

    /// <summary>Drops the references held for the last run and returns the job to its pool. Caller only, after <see cref="CloseGate"/>.</summary>
    protected void Release()
    {
        _exceptions = null;
        _token = default;
        _partitioner = StaticRangePartitioner.Instance;
        ReturnToPool();
    }

    void IThreadPoolWorkItem.Execute()
    {
        int observed = Volatile.Read(ref _gate);
        while (true)
        {
            if ((observed & GateOpen) == 0)
                return;   // a late item: its run is over and the job may already be in someone else's hands

            int previous = Interlocked.CompareExchange(ref _gate, observed + 1, observed);
            if (previous == observed)
                break;
            observed = previous;
        }

        try
        {
            Work();
        }
        finally
        {
            Interlocked.Decrement(ref _gate);
        }
    }

    private void Work()
    {
        if (_stealing)
            WorkStealing();
        else
            WorkStatic();
    }

    // ---------------------------------------------------------------- static partitions

    private void WorkStatic()
    {
        int count = _partitionCount;
        int partition;
        while ((partition = Interlocked.Increment(ref _next) - 1) < count)
        {
            // A claimed partition is always counted, even when a failure or cancellation means it is
            // skipped - otherwise the caller would wait for work nobody is going to do.
            if (Volatile.Read(ref _stop) == NoStop)
                RunPartition(partition);

            if (Interlocked.Decrement(ref _remaining) == 0)
                _done.Set();
        }
    }

    private void RunPartition(int partition)
    {
        try
        {
            for (int index = 0; _partitioner.TryGetRange(_length, _partitionCount, partition, index, out int from, out int to); index++)
            {
                if (!RunRange(_from + from, _from + to, partition))
                    return;
            }
        }
        catch (Exception ex)
        {
            OnException(ex);
        }
    }

    /// <summary>Runs one range, in cancellation-checked blocks if there is a token. False if the loop was stopped.</summary>
    private bool RunRange(int from, int to, int slot)
    {
        if (Volatile.Read(ref _stop) != NoStop)
            return false;

        int block = _blockSize;
        if (block == int.MaxValue)
        {
            InvokeRange(from, to, slot);
            if (AplEventSource.Log.IsEnabled())
                AplEventSource.Log.OnChunk();
            return true;
        }

        while (from < to)
        {
            if (Volatile.Read(ref _stop) != NoStop)
                return false;
            if (_token.IsCancellationRequested)
            {
                RequestStop(StopCanceled);
                return false;
            }

            int end = to - from > block ? from + block : to;
            InvokeRange(from, end, slot);
            if (AplEventSource.Log.IsEnabled())
                AplEventSource.Log.OnChunk();
            from = end;
        }

        return true;
    }

    private void OnException(Exception ex)
    {
        // A body that observes the loop's own token and throws is cancellation, not failure - the
        // same distinction TPL draws.
        if (ex is OperationCanceledException oce && _token.IsCancellationRequested && oce.CancellationToken == _token)
        {
            RequestStop(StopCanceled);
            return;
        }

        lock (_exceptionLock)
            (_exceptions ??= []).Add(ex);

        RequestStop(StopFaulted);
    }

    private void RequestStop(int reason) => Interlocked.CompareExchange(ref _stop, reason, NoStop);

    // ---------------------------------------------------------------- work stealing

    private void InitializeSlots()
    {
        int needed = _workerCount * SlotStride;
        if (_slots.Length < needed)
            _slots = new long[needed];

        for (int w = 0; w < _workerCount; w++)
        {
            if (!_partitioner.TryGetRange(_length, _workerCount, w, 0, out int from, out int to))
                from = to = 0;
            _slots[w * SlotStride] = Pack(from, to);
        }
    }

    private void WorkStealing()
    {
        int self = Interlocked.Increment(ref _next) - 1;
        if (self >= _workerCount)
            return;

        int grain = _grain;
        while (true)
        {
            if (Volatile.Read(ref _stop) != NoStop)
            {
                Drain();
                return;
            }

            if (TryTake(self, grain, out int from, out int to))
            {
                RunGrain(from, to, self);
                continue;
            }

            if (!TrySteal(self))
                return;
        }
    }

    private void RunGrain(int from, int to, int slot)
    {
        try
        {
            RunRange(_from + from, _from + to, slot);
        }
        catch (Exception ex)
        {
            OnException(ex);
        }

        Complete(to - from);
    }

    private void Complete(int iterations)
    {
        if (Interlocked.Add(ref _remaining, -iterations) == 0)
            _done.Set();
    }

    /// <summary>Takes up to <paramref name="grain"/> iterations from the front of this worker's own range.</summary>
    private bool TryTake(int self, int grain, out int from, out int to)
    {
        ref long slot = ref _slots[self * SlotStride];
        while (true)
        {
            long observed = Volatile.Read(ref slot);
            Unpack(observed, out int start, out int end);
            if (start >= end)
            {
                from = to = 0;
                return false;
            }

            int split = end - start > grain ? start + grain : end;
            if (Interlocked.CompareExchange(ref slot, Pack(split, end), observed) == observed)
            {
                from = start;
                to = split;
                return true;
            }
        }
    }

    /// <summary>Moves the back half of the fullest other range into this worker's (empty) range.</summary>
    private bool TrySteal(int self)
    {
        int workers = _workerCount;
        while (true)
        {
            int victim = -1;
            int best = 0;
            long victimValue = 0;
            for (int k = 1; k < workers; k++)
            {
                int w = self + k;
                if (w >= workers)
                    w -= workers;

                long value = Volatile.Read(ref _slots[w * SlotStride]);
                Unpack(value, out int start, out int end);
                if (end - start > best)
                {
                    best = end - start;
                    victim = w;
                    victimValue = value;
                }
            }

            if (victim < 0)
                return false;

            Unpack(victimValue, out int victimStart, out int victimEnd);
            int mid = victimEnd - (victimEnd - victimStart + 1) / 2;
            if (Interlocked.CompareExchange(ref _slots[victim * SlotStride], Pack(victimStart, mid), victimValue) == victimValue)
            {
                // Only this worker ever makes its own slot non-empty, and it is empty now (that is why
                // it is stealing), so no other thread can be racing this write.
                Interlocked.Exchange(ref _slots[self * SlotStride], Pack(mid, victimEnd));
                if (AplEventSource.Log.IsEnabled())
                    AplEventSource.Log.OnSteal();
                return true;
            }
        }
    }

    /// <summary>After a stop: empties every range, counting what was abandoned so the caller stops waiting.</summary>
    private void Drain()
    {
        for (int w = 0; w < _workerCount; w++)
        {
            ref long slot = ref _slots[w * SlotStride];
            while (true)
            {
                long observed = Volatile.Read(ref slot);
                Unpack(observed, out int start, out int end);
                if (start >= end)
                    break;
                if (Interlocked.CompareExchange(ref slot, Pack(end, end), observed) == observed)
                {
                    Complete(end - start);
                    break;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Pack(int from, int to) => (long)(((ulong)(uint)to << 32) | (uint)from);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Unpack(long value, out int from, out int to)
    {
        from = (int)(uint)value;
        to = (int)((ulong)value >> 32);
    }
}

internal static class JobPool
{
    /// <summary>
    /// Jobs kept per body type. A job is back in its pool as soon as its loop returns (see the gate
    /// on <see cref="ParallelJob"/>), so a stream of calls from one thread reuses a single job; more
    /// are needed only for loops of the same body type running at the same time - from several
    /// threads, or nested. Beyond this many, extra jobs are allocated and dropped.
    /// </summary>
    public const int Size = 16;
}

/// <summary>A pooled job for a range body.</summary>
internal sealed class RangeJob<TBody> : ParallelJob
    where TBody : struct, IRangeWorkBody
{
    private static readonly RangeJob<TBody>?[] s_pool = new RangeJob<TBody>?[JobPool.Size];

    private TBody _body;

    public static void Run(in TBody body, in LoopPlan plan)
    {
        RangeJob<TBody> job = Rent();
        job._body = body;
        job.Execute(plan);
        job.CloseGate();
        job.GetOutcome(out List<Exception>? exceptions, out bool canceled, out CancellationToken token);
        job.Release();
        ThrowIfFailed(exceptions, canceled, token);
    }

    protected override void InvokeRange(int fromInclusive, int toExclusive, int slot)
    {
        // A private copy per call: the JIT can keep the body's fields in registers across the inner
        // loop, and a mutable body cannot race with other workers through the shared field.
        TBody body = _body;
        body.Invoke(fromInclusive, toExclusive);
    }

    protected override void ReturnToPool()
    {
        _body = default;
        RangeJob<TBody>?[] pool = s_pool;
        for (int i = 0; i < pool.Length; i++)
        {
            if (Interlocked.CompareExchange(ref pool[i], this, null) is null)
                return;
        }
    }

    private static RangeJob<TBody> Rent()
    {
        RangeJob<TBody>?[] pool = s_pool;
        for (int i = 0; i < pool.Length; i++)
        {
            RangeJob<TBody>? job = Interlocked.Exchange(ref pool[i], null);
            if (job is not null)
                return job;
        }

        return new RangeJob<TBody>();
    }
}

/// <summary>A pooled job for a reduction, with one padded accumulator per partition.</summary>
internal sealed class ReduceJob<T, TBody> : ParallelJob
    where TBody : struct, IReduceBody<T>
{
    private static readonly ReduceJob<T, TBody>?[] s_pool = new ReduceJob<T, TBody>?[JobPool.Size];

    // Accumulators sit a cache line apart so workers writing their own do not invalidate each other's.
    private static readonly int s_stride = Math.Max(1, 64 / Math.Max(1, System.Runtime.CompilerServices.Unsafe.SizeOf<T>()));

    private TBody _body;
    private T _identity = default!;
    private T[] _partials = [];
    private int _slotCount;

    public static T Run(in TBody body, T identity, in LoopPlan plan)
    {
        ReduceJob<T, TBody> job = Rent();
        job._body = body;
        job._identity = identity;
        job.Execute(plan);
        job.CloseGate();
        job.GetOutcome(out List<Exception>? exceptions, out bool canceled, out CancellationToken token);

        T result = identity;
        try
        {
            if (exceptions is null && !canceled)
                result = job.CombinePartials(identity);
        }
        finally
        {
            job.Release();
        }

        ThrowIfFailed(exceptions, canceled, token);
        return result;
    }

    protected override void Prepare(int slotCount)
    {
        _slotCount = slotCount;
        int needed = slotCount * s_stride;
        if (_partials.Length < needed)
            _partials = new T[needed];

        for (int s = 0; s < slotCount; s++)
            _partials[s * s_stride] = _identity;
    }

    protected override void InvokeRange(int fromInclusive, int toExclusive, int slot)
    {
        TBody body = _body;
        ref T accumulator = ref _partials[slot * s_stride];
        accumulator = body.Accumulate(fromInclusive, toExclusive, accumulator);
    }

    private T CombinePartials(T identity)
    {
        TBody body = _body;
        T result = identity;
        for (int s = 0; s < _slotCount; s++)
            result = body.Combine(result, _partials[s * s_stride]);
        return result;
    }

    protected override void ReturnToPool()
    {
        _body = default;
        _identity = default!;
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            Array.Clear(_partials);

        ReduceJob<T, TBody>?[] pool = s_pool;
        for (int i = 0; i < pool.Length; i++)
        {
            if (Interlocked.CompareExchange(ref pool[i], this, null) is null)
                return;
        }
    }

    private static ReduceJob<T, TBody> Rent()
    {
        ReduceJob<T, TBody>?[] pool = s_pool;
        for (int i = 0; i < pool.Length; i++)
        {
            ReduceJob<T, TBody>? job = Interlocked.Exchange(ref pool[i], null);
            if (job is not null)
                return job;
        }

        return new ReduceJob<T, TBody>();
    }
}
