// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ActorNet.Streams;

/// <summary>
/// Operators that change a pipeline's shape rather than its contents: several sources into one,
/// one source into several.
/// </summary>
/// <remarks>
/// <para>
/// Everything on <see cref="ActorStream{T}"/> itself is one-in, one-out, which is what an async
/// enumerable is. These are the two shapes that are not, and they are here rather than on the class
/// because neither has a single obvious receiver - a merge belongs to no one source, and a split
/// returns something that is not a stream at all.
/// </para>
/// <para>
/// Both are built on a channel, for the same reason: the sources run at their own pace and a reader
/// pulls at its own, so something has to sit between them. The channel is bounded, so a fast source
/// waits for a slow reader instead of filling the heap - the same trade the mailboxes make.
/// </para>
/// </remarks>
public static class StreamShapes
{
    /// <summary>How much a shape buffers between its producers and its consumer.</summary>
    /// <remarks>
    /// Small on purpose. The point of a bounded buffer is that a fast producer is made to wait, and
    /// a large one only delays finding out whether the consumer can keep up.
    /// </remarks>
    public const int DefaultBuffer = 64;

    /// <summary>
    /// Reads several sources at once, yielding items in whatever order they arrive.
    /// </summary>
    /// <remarks>
    /// Interleaved, not concatenated: every source is read concurrently, and a source that has
    /// nothing to say does not hold up the others. That is the whole difference from chaining, and
    /// it is why the order of the result is not the order of the arguments.
    /// </remarks>
    public static ActorStream<T> Merge<T>(params ActorStream<T>[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return Merge(DefaultBuffer, sources);
    }

    /// <inheritdoc cref="Merge{T}(ActorStream{T}[])" />
    public static ActorStream<T> Merge<T>(int buffer, params ActorStream<T>[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentOutOfRangeException.ThrowIfLessThan(buffer, 1);

        return ActorStream<T>.From(Merged(sources.Select(s => s.AsAsyncEnumerable()).ToArray(), buffer));
    }

    /// <summary>
    /// The same, for sequences that were never <see cref="ActorStream{T}"/> to begin with.
    /// </summary>
    public static ActorStream<T> Merge<T>(IEnumerable<IAsyncEnumerable<T>> sources, int buffer = DefaultBuffer)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentOutOfRangeException.ThrowIfLessThan(buffer, 1);

        return ActorStream<T>.From(Merged([.. sources], buffer));
    }

    /// <summary>
    /// Splits one source into several, by giving each item a key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The keys have to be known in advance, and that is deliberate. A split that invented a branch
    /// on first sight would have to buffer everything for a branch nobody is reading yet, and the
    /// buffer would be unbounded because nothing can say how long "yet" is. Naming the branches up
    /// front makes the memory bounded and the failure mode obvious.
    /// </para>
    /// <para>
    /// An item whose key is not one of them is dropped. There is nowhere to put it, and the
    /// alternative - failing the whole pipeline over one item - is worse for the thing this is
    /// usually doing, which is fanning traffic out by type or by tenant.
    /// </para>
    /// <para>
    /// Every branch must be consumed, and concurrently. They share one reader of the source, so a
    /// branch nobody reads fills its buffer and then stops the source, which stops the others.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<TKey, ActorStream<T>> Split<T, TKey>(
        ActorStream<T> source, Func<T, TKey> key, IReadOnlyCollection<TKey> branches, int buffer = DefaultBuffer)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentOutOfRangeException.ThrowIfLessThan(buffer, 1);

        if (branches.Count == 0) throw new ArgumentException("A split with no branches has nowhere to put anything.", nameof(branches));

        var channels = branches.ToDictionary(
            branch => branch,
            _ => Channel.CreateBounded<T>(new BoundedChannelOptions(buffer)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            }));

        // One reader of the source, started lazily by whichever branch is enumerated first. Started
        // eagerly it would pull from the source before anybody asked, which for a source with side
        // effects - a queue, a socket - is not the same thing at all.
        var pump = new Lazy<Task>(() => Task.Run(() => PumpAsync(source, key, channels)));

        return channels.ToDictionary(
            pair => pair.Key,
            pair => ActorStream<T>.From(Branch(pair.Value.Reader, pump)));
    }

    /// <summary>
    /// Runs several sources into one actor, which is the common reason to merge.
    /// </summary>
    public static Task<long> FanInAsync<T>(
        IActorSystem system, ActorId target, IEnumerable<ActorStream<T>> sources, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return Merge<T>([.. sources]).ToActorAsync(system, target, cancellationToken);
    }

    private static async IAsyncEnumerable<T> Merged<T>(
        IAsyncEnumerable<T>[] sources, int buffer, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (sources.Length == 0) yield break;

        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(buffer)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var readers = sources.Select(source => Task.Run(async () =>
        {
            await foreach (var item in source.WithCancellation(stopping.Token).ConfigureAwait(false))
                await channel.Writer.WriteAsync(item, stopping.Token).ConfigureAwait(false);
        }, stopping.Token)).ToArray();

        // Completed when every source has finished, and faulted if any of them did. The consumer
        // then sees the failure at the end of the sequence rather than never - a merge that
        // swallowed a source's exception would report a short stream as a complete one.
        var all = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(readers).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            // A consumer that stopped early - a Take, a break - leaves the sources running. This is
            // what ends them, and awaiting afterwards is what stops the pipeline outliving the loop.
            await stopping.CancelAsync().ConfigureAwait(false);
            try { await all.ConfigureAwait(false); } catch (Exception) { /* already reported, or cancelled */ }
        }
    }

    private static async Task PumpAsync<T, TKey>(
        ActorStream<T> source, Func<T, TKey> key, Dictionary<TKey, Channel<T>> channels)
        where TKey : notnull
    {
        try
        {
            await foreach (var item in source.AsAsyncEnumerable().ConfigureAwait(false))
            {
                // An item whose key names no branch has nowhere to go. Dropped rather than throwing:
                // failing a whole pipeline over one unroutable item is the worse of the two.
                if (channels.TryGetValue(key(item), out var channel))
                    await channel.Writer.WriteAsync(item).ConfigureAwait(false);
            }

            foreach (var channel in channels.Values) channel.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            // Every branch sees the same failure. A split that faulted one branch and quietly ended
            // the others would look like a clean finish everywhere but one place.
            foreach (var channel in channels.Values) channel.Writer.TryComplete(ex);
        }
    }

    private static async IAsyncEnumerable<T> Branch<T>(
        ChannelReader<T> reader, Lazy<Task> pump, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = pump.Value;

        await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }
}
