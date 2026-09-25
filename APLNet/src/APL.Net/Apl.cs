// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet.Core;

namespace AplNet;

/// <summary>
/// APL.Net's entry point: data-parallel loops with the call shape of <see cref="Parallel"/> and far
/// less overhead per iteration.
/// </summary>
/// <remarks>
/// <para>
/// Each operation comes in two flavours. The delegate overloads are drop-in replacements for
/// <c>Parallel.For</c> / <c>Parallel.ForEach</c>. The generic struct overloads
/// (<c>TBody : struct, IWorkBody</c>) are the zero-allocation path: the JIT specialises the loop
/// for your struct and inlines its <c>Invoke</c>, so there is no delegate call per iteration at all.
/// <see cref="ForRange{TBody}"/> goes further and hands the body whole sub-ranges.
/// </para>
/// <para>
/// <b>Behavioural difference from TPL: the <see cref="ExecutionContext"/> does not flow to pool
/// workers.</b> Bodies running on them do not see the caller's <see cref="AsyncLocal{T}"/> values or
/// flowed culture/impersonation. This is deliberate - capturing and restoring the context is pure
/// overhead for numeric loops - but a body that relies on ambient state must receive it explicitly,
/// or use TPL. The async wrappers (<see cref="ForEachAsync{T}(IEnumerable{T}, Func{T, CancellationToken, ValueTask}, AplOptions?)"/>
/// and friends) are the exception: they are built on TPL and do flow it.
/// </para>
/// <para>
/// Work is split statically by default (<see cref="Core.Partitioners.StaticRangePartitioner"/>). If
/// iteration costs vary widely, pass <see cref="Core.Partitioners.WorkStealingPartitioner"/> in
/// <see cref="AplOptions.Partitioner"/>.
/// </para>
/// </remarks>
public static class Apl
{
    // ---------------------------------------------------------------- For

    /// <summary>Runs <paramref name="body"/> for every index in <c>[fromInclusive, toExclusive)</c>. Drop-in for <c>Parallel.For</c>.</summary>
    /// <exception cref="AggregateException">The body threw; contains every exception thrown.</exception>
    /// <exception cref="OperationCanceledException">The options' token was canceled.</exception>
    public static void For(int fromInclusive, int toExclusive, Action<int> body, AplOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ParallelExecutor.Run(fromInclusive, toExclusive, new DelegateBody(body), options, defaultMinChunk: 1);
    }

    /// <summary>
    /// Runs a struct body for every index in <c>[fromInclusive, toExclusive)</c>. Allocation-free in
    /// steady state, and the JIT inlines the body's <see cref="IWorkBody.Invoke"/> into the loop.
    /// </summary>
    /// <exception cref="AggregateException">The body threw; contains every exception thrown.</exception>
    /// <exception cref="OperationCanceledException">The options' token was canceled.</exception>
    public static void For<TBody>(int fromInclusive, int toExclusive, TBody body, AplOptions? options = null)
        where TBody : struct, IWorkBody =>
        ParallelExecutor.For(fromInclusive, toExclusive, body, options);

    // ---------------------------------------------------------------- ForRange

    /// <summary>Runs <paramref name="body"/> once per sub-range; the ranges are disjoint and cover <c>[fromInclusive, toExclusive)</c>.</summary>
    /// <exception cref="AggregateException">The body threw; contains every exception thrown.</exception>
    /// <exception cref="OperationCanceledException">The options' token was canceled.</exception>
    public static void ForRange(int fromInclusive, int toExclusive, Action<int, int> body, AplOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ParallelExecutor.Run(fromInclusive, toExclusive, new RangeDelegateBody(body), options, defaultMinChunk: 1);
    }

    /// <summary>
    /// Runs a struct body once per sub-range. The fastest loop shape: the body can slice spans (no
    /// bounds checks), hoist invariants and use SIMD across its whole range.
    /// </summary>
    /// <exception cref="AggregateException">The body threw; contains every exception thrown.</exception>
    /// <exception cref="OperationCanceledException">The options' token was canceled.</exception>
    public static void ForRange<TBody>(int fromInclusive, int toExclusive, TBody body, AplOptions? options = null)
        where TBody : struct, IRangeWorkBody =>
        ParallelExecutor.ForRange(fromInclusive, toExclusive, body, options);

    // ---------------------------------------------------------------- ForEach

    /// <summary>Runs <paramref name="body"/> for every element of <paramref name="source"/>. Drop-in for <c>Parallel.ForEach</c> over an array.</summary>
    /// <exception cref="AggregateException">The body threw; contains every exception thrown.</exception>
    /// <exception cref="OperationCanceledException">The options' token was canceled.</exception>
    public static void ForEach<T>(T[] source, Action<T> body, AplOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(body);
        ParallelExecutor.Run(0, source.Length, new ArrayValueDelegateBody<T>(source, body), options, defaultMinChunk: 1);
    }

    /// <summary>Runs <paramref name="body"/> for every element of <paramref name="source"/>, by reference so it can be updated in place.</summary>
    /// <exception cref="AggregateException">The body threw; contains every exception thrown.</exception>
    /// <exception cref="OperationCanceledException">The options' token was canceled.</exception>
    public static void ForEach<T>(T[] source, RefItemAction<T> body, AplOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(body);
        ParallelExecutor.Run(0, source.Length, new ArrayRefDelegateBody<T>(source, body), options, defaultMinChunk: 1);
    }

    /// <summary>Runs a struct body for every element of <paramref name="source"/>, by reference. Allocation-free in steady state.</summary>
    /// <exception cref="AggregateException">The body threw; contains every exception thrown.</exception>
    /// <exception cref="OperationCanceledException">The options' token was canceled.</exception>
    public static void ForEach<T, TBody>(T[] source, TBody body, AplOptions? options = null)
        where TBody : struct, IWorkBody<T>
    {
        ArgumentNullException.ThrowIfNull(source);
        ParallelExecutor.Run(0, source.Length, new ArrayItemBody<T, TBody>(source, body), options, defaultMinChunk: 1);
    }

    /// <summary>Runs a struct body for every element of <paramref name="source"/>, by reference. Allocation-free in steady state.</summary>
    /// <exception cref="AggregateException">The body threw; contains every exception thrown.</exception>
    /// <exception cref="OperationCanceledException">The options' token was canceled.</exception>
    public static void ForEach<T, TBody>(Memory<T> source, TBody body, AplOptions? options = null)
        where TBody : struct, IWorkBody<T> =>
        ParallelExecutor.Run(0, source.Length, new MemoryItemBody<T, TBody>(source, body), options, defaultMinChunk: 1);

    // ---------------------------------------------------------------- Reduce

    /// <summary>
    /// Folds <c>[fromInclusive, toExclusive)</c> into one value. <paramref name="accumulate"/> folds a
    /// sub-range into a worker's running value; <paramref name="combine"/> merges two workers' values and
    /// must be associative, with <paramref name="identity"/> as its identity.
    /// </summary>
    /// <exception cref="AggregateException">A callback threw; contains every exception thrown.</exception>
    /// <exception cref="OperationCanceledException">The options' token was canceled.</exception>
    public static T Reduce<T>(int fromInclusive, int toExclusive, T identity, Func<int, int, T, T> accumulate, Func<T, T, T> combine, AplOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(accumulate);
        ArgumentNullException.ThrowIfNull(combine);
        return ParallelExecutor.RunReduce(fromInclusive, toExclusive, identity, new DelegateReduceBody<T>(accumulate, combine), options, defaultMinChunk: 1);
    }

    /// <summary>Folds <c>[fromInclusive, toExclusive)</c> into one value with a struct body. Allocation-free in steady state.</summary>
    /// <exception cref="AggregateException">The body threw; contains every exception thrown.</exception>
    /// <exception cref="OperationCanceledException">The options' token was canceled.</exception>
    public static T Reduce<T, TBody>(int fromInclusive, int toExclusive, T identity, TBody body, AplOptions? options = null)
        where TBody : struct, IReduceBody<T> =>
        ParallelExecutor.Reduce(fromInclusive, toExclusive, identity, body, options);

    // ---------------------------------------------------------------- async

    /// <summary>
    /// Runs an asynchronous body for every index. A thin wrapper over <c>Parallel.ForAsync</c>
    /// that accepts <see cref="AplOptions"/>: asynchronous bodies are dominated by I/O, not by dispatch,
    /// so there is nothing for APL.Net's machinery to win. Unlike the synchronous loops this does flow
    /// the <see cref="ExecutionContext"/>.
    /// </summary>
    public static Task ForAsync(int fromInclusive, int toExclusive, Func<int, CancellationToken, ValueTask> body, AplOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Parallel.ForAsync(fromInclusive, toExclusive, ToParallelOptions(options), body);
    }

    /// <summary>Runs an asynchronous body for every element. See <see cref="ForAsync"/> for why this wraps TPL.</summary>
    public static Task ForEachAsync<T>(IEnumerable<T> source, Func<T, CancellationToken, ValueTask> body, AplOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(body);
        return Parallel.ForEachAsync(source, ToParallelOptions(options), body);
    }

    /// <summary>Runs an asynchronous body for every element of an async stream. See <see cref="ForAsync"/> for why this wraps TPL.</summary>
    public static Task ForEachAsync<T>(IAsyncEnumerable<T> source, Func<T, CancellationToken, ValueTask> body, AplOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(body);
        return Parallel.ForEachAsync(source, ToParallelOptions(options), body);
    }

    private static ParallelOptions ToParallelOptions(AplOptions? options)
    {
        options ??= AplOptions.Default;
        return new ParallelOptions
        {
            MaxDegreeOfParallelism = options.GetEffectiveDegreeOfParallelism(),
            CancellationToken = options.CancellationToken,
        };
    }
}
