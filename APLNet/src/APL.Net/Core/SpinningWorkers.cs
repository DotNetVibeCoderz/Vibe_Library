// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AplNet.Core;

/// <summary>Where <see cref="SpinningWorkers"/> pins its threads.</summary>
public enum WorkerAffinity
{
    /// <summary>No pinning: the OS schedules the threads wherever it likes. The default.</summary>
    None,

    /// <summary>
    /// Each worker is pinned to one logical processor, in processor order. Worker <c>k</c> runs on the
    /// processor after worker <c>k - 1</c>'s, so with stable partition placement the same slice of an
    /// array is processed by the same core on every call and stays in that core's cache.
    /// </summary>
    Core,

    /// <summary>
    /// Each worker is pinned to the NUMA node of the processor <see cref="Core"/> would have chosen,
    /// and may run on any processor of that node. Contiguous partitions go to workers on the same
    /// node, so the memory a node first touches is the memory it keeps working on. On a machine with
    /// one node this only keeps the threads off nothing; it is meant for multi-socket servers.
    /// </summary>
    NumaNode,
}

/// <summary>
/// An opt-in set of dedicated worker threads that keep spinning between loops instead of returning
/// to the thread pool. Pass it in <see cref="AplOptions.Workers"/>.
/// </summary>
/// <remarks>
/// <para>
/// Waking pool threads costs a few microseconds per loop. That is noise for a loop that runs for
/// milliseconds, but it dominates very small loops that run very often, such as the per-tick update
/// of a game or a simulation. These workers busy-wait for <see cref="SpinDuration"/> after each loop,
/// so the next loop finds them awake. That trades CPU time (a spinning worker keeps a core busy) for
/// latency. After the spin duration with nothing to do they block, and cost nothing until the next
/// loop wakes them.
/// </para>
/// <para>
/// Placement is stable: on every loop, worker <c>k</c> first tries to claim the partition after the
/// caller's, the <c>(k + 1)</c>-th, and only then helps with the others. Combined with
/// <see cref="WorkerAffinity"/>, the same part of the data is processed by the same core (or NUMA
/// node) call after call. Placement applies to the static and striped partitioners; work stealing
/// moves work between workers by design.
/// </para>
/// <para>
/// Safe to share between threads and loops. Each worker has a one-item mailbox. A loop that finds
/// it full (another caller's loop has not been picked up yet) queues that share to the thread pool
/// instead. A job left waiting while its worker is busy, for example in a nested loop, simply runs
/// late, and by then the caller has usually done its share. A busy group can slow a loop down, but
/// it never blocks it. Dispose the group to stop its threads; loops that still
/// reference it afterwards fall back to the thread pool.
/// </para>
/// </remarks>
public sealed class SpinningWorkers : IDisposable
{
    private readonly Worker[] _workers;
    private readonly Slot[] _slots;
    private readonly long _spinTicks;
    private volatile bool _disposed;

    /// <summary>
    /// Starts <paramref name="threadCount"/> spinning worker threads.
    /// </summary>
    /// <param name="threadCount">The number of threads, not counting the callers that will use them. -1 means <c>Environment.ProcessorCount - 1</c>, which serves a loop at the default degree of parallelism.</param>
    /// <param name="spinDuration">How long a worker keeps spinning after its last loop before it blocks. <c>null</c> means 1 ms; <see cref="Timeout.InfiniteTimeSpan"/> spins forever.</param>
    /// <param name="affinity">Where to pin the threads. Pinning is applied on Windows and Linux; elsewhere it is ignored, and <see cref="IsAffinityApplied"/> says so.</param>
    public SpinningWorkers(int threadCount = -1, TimeSpan? spinDuration = null, WorkerAffinity affinity = WorkerAffinity.None)
    {
        if (threadCount == -1)
            threadCount = Math.Max(1, Environment.ProcessorCount - 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(threadCount, 1);

        TimeSpan spin = spinDuration ?? TimeSpan.FromMilliseconds(1);
        if (spin != Timeout.InfiniteTimeSpan && spin < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(spinDuration), spin, "The spin duration must be non-negative, or Timeout.InfiniteTimeSpan.");

        SpinDuration = spin;
        Affinity = affinity;
        _spinTicks = spin == Timeout.InfiniteTimeSpan ? long.MaxValue : (long)(spin.TotalSeconds * Stopwatch.Frequency);

        int[] placement = affinity == WorkerAffinity.None ? [] : AplTopology.GetWorkerPlacement(threadCount);
        _workers = new Worker[threadCount];
        _slots = new Slot[threadCount];
        int pinned = 0;
        using var started = new CountdownEvent(threadCount);
        for (int k = 0; k < threadCount; k++)
        {
            var worker = new Worker(this, index: k, processor: placement.Length > 0 ? placement[k] : -1, started);
            _workers[k] = worker;
            worker.Start();
        }

        started.Wait();
        foreach (Worker worker in _workers)
            pinned += worker.Pinned ? 1 : 0;
        IsAffinityApplied = affinity != WorkerAffinity.None && pinned == threadCount;
    }

    /// <summary>The number of worker threads.</summary>
    public int ThreadCount => _workers.Length;

    /// <summary>How long a worker spins after its last loop before it blocks.</summary>
    public TimeSpan SpinDuration { get; }

    /// <summary>The pinning that was requested.</summary>
    public WorkerAffinity Affinity { get; }

    /// <summary>Every thread was pinned as <see cref="Affinity"/> asked. False when no pinning was asked for, or the platform does not support it.</summary>
    public bool IsAffinityApplied { get; }

    /// <summary>Stops the threads after they finish any loop they are part of. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (Worker worker in _workers)
            worker.Wake.Set();
        foreach (Worker worker in _workers)
        {
            if (worker.Thread != Thread.CurrentThread)
                worker.Thread.Join();
        }
    }

    /// <summary>
    /// Hands <paramref name="job"/> to up to <paramref name="count"/> workers, in order, and queues
    /// whatever share no idle worker could take to the thread pool.
    /// </summary>
    internal void Dispatch(ParallelJob job, int count)
    {
        int i = 0;
        if (!_disposed)
        {
            Slot[] slots = _slots;
            for (; i < count && i < slots.Length; i++)
            {
                ref Slot slot = ref slots[i];
                if (Interlocked.CompareExchange(ref slot.Item, job, null) is null)
                {
                    // Pairs with the worker's write of Sleeping before its last look at Item: either it
                    // sees the job, or this sees it asleep and wakes it.
                    if (Volatile.Read(ref slot.Sleeping) != 0)
                        _workers[i].Wake.Set();
                }
                else
                {
                    ThreadPool.UnsafeQueueUserWorkItem(job, preferLocal: false);
                }
            }
        }

        for (; i < count; i++)
            ThreadPool.UnsafeQueueUserWorkItem(job, preferLocal: false);
    }

    /// <summary>What a caller and a worker exchange, alone on its cache line so callers handing work to one worker do not disturb the next one spinning.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct Slot
    {
        [FieldOffset(64)] public ParallelJob? Item;
        [FieldOffset(72)] public int Sleeping;
    }

    private sealed class Worker(SpinningWorkers owner, int index, int processor, CountdownEvent started)
    {
        public readonly ManualResetEventSlim Wake = new(initialState: false);
        public Thread Thread = null!;
        public bool Pinned;

        public void Start()
        {
            Thread = new Thread(Run, maxStackSize: 0)
            {
                IsBackground = true,
                Name = $"APL.Net spinning worker {index + 1}",
            };
            Thread.UnsafeStart();
        }

        private void Run()
        {
            if (processor >= 0)
                Pinned = AplTopology.TryPinCurrentThread(processor, owner.Affinity);
            started.Signal();

            // The caller's home is partition 0, so worker k's is k + 1.
            int home = index + 1;
            ref Slot slot = ref owner._slots[index];
            long spinTicks = owner._spinTicks;
            long lastWork = Stopwatch.GetTimestamp();
            while (true)
            {
                // The slot is a one-item mailbox: the job is taken out before it runs, so the next
                // loop can be handed over while this one is still finishing. A job that turns out
                // to be over by the time it runs is a late item, which the job's gate turns away.
                ParallelJob? job = Volatile.Read(ref slot.Item) is null ? null : Interlocked.Exchange(ref slot.Item, null);
                if (job is not null)
                {
                    job.RunAsWorker(home);
                    lastWork = Stopwatch.GetTimestamp();
                    continue;
                }

                if (owner._disposed)
                    return;

                if (Stopwatch.GetTimestamp() - lastWork < spinTicks)
                {
                    Thread.SpinWait(16);
                    continue;
                }

                Wake.Reset();
                Interlocked.Exchange(ref slot.Sleeping, 1);
                if (Volatile.Read(ref slot.Item) is null && !owner._disposed)
                    Wake.Wait();
                Interlocked.Exchange(ref slot.Sleeping, 0);
                lastWork = Stopwatch.GetTimestamp();
            }
        }
    }
}
