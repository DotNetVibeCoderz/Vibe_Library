// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ActorNet.Streams;

/// <summary>
/// A reactive pipeline that ends by delivering to actors.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately small: this is <see cref="IAsyncEnumerable{T}"/> with a few operators and an actor
/// sink, not a port of Akka Streams. That is the honest scope - it covers "pull from a source,
/// shape it, route each item to the actor that owns it", which is what the event-driven pipelines
/// in the requirements actually need, and it composes with every other async-enumerable in .NET
/// instead of inventing a parallel world.
/// </para>
/// <para>
/// Backpressure is whatever the consumer's pace makes it: nothing is buffered unless
/// <see cref="Buffer"/> asks for it, and a bounded mailbox on the sink propagates the slowdown all
/// the way back to the source.
/// </para>
/// </remarks>
public sealed class ActorStream<T>(IAsyncEnumerable<T> source)
{
    private readonly IAsyncEnumerable<T> _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>Wraps any async sequence.</summary>
    public static ActorStream<T> From(IAsyncEnumerable<T> source) => new(source);

    /// <summary>Wraps a synchronous sequence.</summary>
    public static ActorStream<T> From(IEnumerable<T> source) => new(Iterate(source));

    /// <summary>Ticks <paramref name="value"/> on an interval, forever.</summary>
    public static ActorStream<T> Interval(TimeSpan period, Func<long, T> value) => new(Ticks(period, value));

    /// <summary>The underlying sequence, for interop with anything that takes one.</summary>
    public IAsyncEnumerable<T> AsAsyncEnumerable() => _source;

    /// <summary>Keeps only the items that satisfy <paramref name="predicate"/>.</summary>
    public ActorStream<T> Where(Func<T, bool> predicate) => new(Filter(_source, predicate));

    /// <summary>Projects each item.</summary>
    public ActorStream<TOut> Select<TOut>(Func<T, TOut> selector) => new(Map(_source, selector));

    /// <summary>Projects each item asynchronously, one at a time, preserving order.</summary>
    public ActorStream<TOut> SelectAsync<TOut>(Func<T, CancellationToken, ValueTask<TOut>> selector) => new(MapAsync(_source, selector));

    /// <summary>Stops after <paramref name="count"/> items.</summary>
    public ActorStream<T> Take(int count) => new(Limit(_source, count));

    /// <summary>
    /// Groups items into batches of at most <paramref name="size"/>, flushing early when
    /// <paramref name="within"/> elapses.
    /// </summary>
    /// <remarks>
    /// The time bound is what keeps a partly-filled batch from sitting forever on a quiet stream -
    /// the failure mode of a size-only batcher.
    /// </remarks>
    public ActorStream<IReadOnlyList<T>> Batch(int size, TimeSpan? within = null) => new(Batched(_source, size, within));

    /// <summary>Decouples producer and consumer with a bounded buffer.</summary>
    public ActorStream<T> Buffer(int capacity) => new(Buffered(_source, capacity));

    /// <summary>Runs a side effect per item, passing it through. For logging and metrics.</summary>
    public ActorStream<T> Tap(Action<T> effect) => new(Map(_source, item =>
    {
        effect(item);
        return item;
    }));

    /// <summary>
    /// Sends each item to the actor chosen by <paramref name="route"/> and returns how many were
    /// sent.
    /// </summary>
    /// <remarks>
    /// This is where a stream meets the actor model: routing by key means each item lands on the
    /// single activation that owns that key, so per-key ordering and single-writer state come for
    /// free.
    /// </remarks>
    public async Task<long> ToActorsAsync(IActorSystem system, Func<T, ActorId> route, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(route);

        var sent = 0L;
        await foreach (var item in _source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await system.TellAsync(route(item), item!, default, cancellationToken).ConfigureAwait(false);
            sent++;
        }

        return sent;
    }

    /// <summary>Sends every item to one actor.</summary>
    public Task<long> ToActorAsync(IActorSystem system, ActorId target, CancellationToken cancellationToken = default) =>
        ToActorsAsync(system, _ => target, cancellationToken);

    /// <summary>Runs the pipeline for its side effects.</summary>
    public async Task<long> RunAsync(Func<T, CancellationToken, ValueTask>? sink = null, CancellationToken cancellationToken = default)
    {
        var count = 0L;
        await foreach (var item in _source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (sink is not null) await sink(item, cancellationToken).ConfigureAwait(false);
            count++;
        }

        return count;
    }

    private static async IAsyncEnumerable<T> Iterate(IEnumerable<T> source)
    {
        foreach (var item in source) yield return item;
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<T> Ticks(TimeSpan period, Func<long, T> value, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var timer = new PeriodicTimer(period);
        var tick = 0L;
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            yield return value(tick++);
    }

    private static async IAsyncEnumerable<T> Filter(IAsyncEnumerable<T> source, Func<T, bool> predicate, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            if (predicate(item)) yield return item;
    }

    private static async IAsyncEnumerable<TOut> Map<TOut>(IAsyncEnumerable<T> source, Func<T, TOut> selector, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return selector(item);
    }

    private static async IAsyncEnumerable<TOut> MapAsync<TOut>(IAsyncEnumerable<T> source, Func<T, CancellationToken, ValueTask<TOut>> selector, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return await selector(item, cancellationToken).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<T> Limit(IAsyncEnumerable<T> source, int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (count <= 0) yield break;

        var taken = 0;
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
            if (++taken >= count) yield break;
        }
    }

    private static async IAsyncEnumerable<IReadOnlyList<T>> Batched(IAsyncEnumerable<T> source, int size, TimeSpan? within, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        var batch = new List<T>(size);
        var deadline = within is { } window ? DateTimeOffset.UtcNow + window : DateTimeOffset.MaxValue;

        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            batch.Add(item);

            // The time check runs per item rather than on a timer: a batcher that fires on a timer
            // needs a second thread and a lock over the list, and this is enough for a stream that
            // is producing at all.
            if (batch.Count < size && DateTimeOffset.UtcNow < deadline) continue;

            yield return batch.ToArray();
            batch.Clear();
            if (within is { } next) deadline = DateTimeOffset.UtcNow + next;
        }

        if (batch.Count > 0) yield return batch.ToArray();
    }

    private static async IAsyncEnumerable<T> Buffered(IAsyncEnumerable<T> source, int capacity, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        var pump = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                    await channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                // Completing with the fault is what lets the consumer see the producer's error
                // instead of a stream that just ends early.
                channel.Writer.TryComplete(ex);
            }
        }, cancellationToken);

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;

        await pump.ConfigureAwait(false);
    }
}

/// <summary>Entry points for building a stream.</summary>
public static class ActorStream
{
    /// <summary>Wraps an async sequence.</summary>
    public static ActorStream<T> From<T>(IAsyncEnumerable<T> source) => ActorStream<T>.From(source);

    /// <summary>Wraps a synchronous sequence.</summary>
    public static ActorStream<T> From<T>(IEnumerable<T> source) => ActorStream<T>.From(source);

    /// <summary>Ticks on an interval.</summary>
    public static ActorStream<T> Interval<T>(TimeSpan period, Func<long, T> value) => ActorStream<T>.Interval(period, value);
}
