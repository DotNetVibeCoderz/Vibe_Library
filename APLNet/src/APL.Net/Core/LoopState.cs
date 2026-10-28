// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace AplNet.Core;

/// <summary>
/// Lets an iteration end its loop early, like <see cref="ParallelLoopState"/>. Passed by value to
/// each iteration of a stateful loop; it is two fields wide and allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Stop"/> asks every worker to stop as soon as it can: no iteration starts after a worker
/// has seen the request. <see cref="Break"/> promises only that the iterations <em>below</em> this
/// one will still run; those above it may be skipped. Both are checked before every iteration, so
/// an iteration that has already started always finishes.
/// </para>
/// <para>
/// As with TPL, a loop may use one or the other, not both: calling <see cref="Break"/> after
/// <see cref="Stop"/> (or the reverse) throws <see cref="InvalidOperationException"/>. The value is
/// valid only during the iteration it was passed to.
/// </para>
/// </remarks>
public readonly struct AplLoopState
{
    private readonly LoopStateCore _core;
    private readonly int _index;

    internal AplLoopState(LoopStateCore core, int index)
    {
        _core = core;
        _index = index;
    }

    /// <summary>The index of the iteration this state was passed to.</summary>
    public int CurrentIteration => _index;

    /// <summary><see cref="Stop"/> has been called by some iteration.</summary>
    public bool IsStopped => _core.IsStopped;

    /// <summary>The lowest index any iteration has called <see cref="Break"/> from, or <c>null</c>.</summary>
    public int? LowestBreakIteration => _core.LowestBreakIteration;

    /// <summary>
    /// The current iteration would not be started now: the loop was stopped, or broken at a lower
    /// index. Long-running iterations can poll it and return early.
    /// </summary>
    public bool ShouldExitCurrentIteration => _core.ShouldExit(_index);

    /// <summary>Ends the loop as soon as possible. Iterations already running finish.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Break"/> was already called.</exception>
    public void Stop() => _core.Stop();

    /// <summary>
    /// Ends the loop after every iteration below this one has run. Iterations above it that have
    /// not started are skipped.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="Stop"/> was already called.</exception>
    public void Break() => _core.Break(_index);
}

/// <summary>How a stateful loop ended: the equivalent of <see cref="ParallelLoopResult"/>.</summary>
public readonly struct AplLoopResult
{
    internal AplLoopResult(bool isCompleted, int? lowestBreakIteration)
    {
        IsCompleted = isCompleted;
        LowestBreakIteration = lowestBreakIteration;
    }

    /// <summary>Every iteration ran: neither <see cref="AplLoopState.Stop"/> nor <see cref="AplLoopState.Break"/> was called.</summary>
    public bool IsCompleted { get; }

    /// <summary>The lowest index <see cref="AplLoopState.Break"/> was called from, or <c>null</c> if it was not called.</summary>
    public int? LowestBreakIteration { get; }
}

/// <summary>
/// The state shared by every iteration of one stateful loop. <see cref="_limit"/> folds Stop and
/// Break into one field so the per-iteration check is a single load and compare: an iteration runs
/// only while its index is at most the limit. Stop drops the limit to <see cref="int.MinValue"/>;
/// Break lowers it to the breaking index.
/// </summary>
internal sealed class LoopStateCore
{
    private const int None = 0;
    private const int Stopped = 1;
    private const int Broken = 2;

    [ThreadStatic]
    private static LoopStateCore? t_cached;

    private int _limit = int.MaxValue;
    private int _mode;

    public bool IsStopped => Volatile.Read(ref _mode) == Stopped;

    public int? LowestBreakIteration => Volatile.Read(ref _mode) == Broken ? Volatile.Read(ref _limit) : null;

    /// <summary>One per calling thread, so a steady stream of stateful loops allocates nothing; a nested loop gets its own.</summary>
    public static LoopStateCore Rent()
    {
        LoopStateCore core = t_cached ?? new LoopStateCore();
        t_cached = null;
        core._limit = int.MaxValue;
        core._mode = None;
        return core;
    }

    public static void Return(LoopStateCore core) => t_cached = core;

    public AplLoopResult ToResult() => new(Volatile.Read(ref _mode) == None, LowestBreakIteration);

    public bool ShouldExit(int index) => index > Volatile.Read(ref _limit);

    public void Stop()
    {
        if (Interlocked.CompareExchange(ref _mode, Stopped, None) == Broken)
            throw new InvalidOperationException("Stop was called after Break; a loop may use one or the other.");
        Volatile.Write(ref _limit, int.MinValue);
    }

    public void Break(int index)
    {
        if (Interlocked.CompareExchange(ref _mode, Broken, None) == Stopped)
            throw new InvalidOperationException("Break was called after Stop; a loop may use one or the other.");

        int observed = Volatile.Read(ref _limit);
        while (index < observed)
        {
            int seen = Interlocked.CompareExchange(ref _limit, index, observed);
            if (seen == observed)
                return;
            observed = seen;
        }
    }
}
