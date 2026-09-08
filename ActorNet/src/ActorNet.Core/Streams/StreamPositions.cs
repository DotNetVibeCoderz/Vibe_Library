// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.CompilerServices;
using ActorNet.Persistence;

namespace ActorNet.Streams;

/// <summary>How far a named stream has been read.</summary>
/// <param name="Stream">The name the position is kept under.</param>
/// <param name="Offset">The offset of the last item handled.</param>
/// <param name="UpdatedAt">When it was last written.</param>
public sealed record StreamPosition(string Stream, long Offset, DateTimeOffset UpdatedAt);

/// <summary>Where a stream's progress is remembered between runs.</summary>
public interface IStreamPositions
{
    /// <summary>The last offset handled for <paramref name="stream"/>, or zero.</summary>
    Task<long> LoadAsync(string stream, CancellationToken cancellationToken = default);

    /// <summary>Records that <paramref name="stream"/> has been handled up to <paramref name="offset"/>.</summary>
    Task SaveAsync(string stream, long offset, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stream positions kept in the ordinary state store, so they last as long as anything else does.
/// </summary>
/// <remarks>
/// A position is a tiny piece of durable state keyed by a name, which is exactly what
/// <see cref="IStateStore"/> is. Giving positions a store of their own would mean a second thing to
/// configure, a second thing to back up, and a second provider per database.
/// </remarks>
public sealed class StoredStreamPositions(IStateStore store, string prefix = "stream-position/") : IStreamPositions
{
    /// <inheritdoc />
    public async Task<long> LoadAsync(string stream, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(stream);

        var stored = await store.ReadAsync<StreamPosition>(prefix + stream, cancellationToken).ConfigureAwait(false);
        return stored?.Value?.Offset ?? 0;
    }

    /// <inheritdoc />
    public async Task SaveAsync(string stream, long offset, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(stream);

        // Written without a version check on purpose. Two readers of one named stream is a
        // configuration mistake rather than a race to arbitrate, and refusing the write would turn
        // it into a stalled pipeline instead of a duplicate one.
        await store.WriteAsync(
            prefix + stream,
            new StreamPosition(stream, offset, DateTimeOffset.UtcNow),
            IStateStore.AnyVersion,
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Resuming a stream where the last run left off.
/// </summary>
/// <remarks>
/// <para>
/// Only for a source that replays: the same items, in the same order, on the next run. A journal
/// read does that. A socket, a timer and a queue that acknowledges do not, and skipping the first
/// <em>n</em> items of one of those skips whatever happened to arrive first.
/// </para>
/// <para>
/// Delivery is at least once. The position is written after an item has been handled, so a crash
/// between the two replays that item on the next run - which is the safe direction, and the reason
/// a handler behind this has to be idempotent. Writing the position first would make it at most
/// once, and lose the item instead.
/// </para>
/// </remarks>
public static class ResumableStreams
{
    /// <summary>
    /// Skips what has already been handled, and records progress as it goes.
    /// </summary>
    /// <param name="source">The stream to resume. Must replay the same items in the same order.</param>
    /// <param name="positions">Where progress is kept.</param>
    /// <param name="name">The name this stream's position is kept under.</param>
    /// <param name="offsetOf">
    /// The offset an item represents. Defaults to counting items, which is right for a source whose
    /// items carry no position of their own - and wrong for one whose items do, because a count
    /// cannot survive the source itself being compacted.
    /// </param>
    /// <param name="checkpointEvery">
    /// Items handled between writes of the position. One is the safest and the most expensive; a
    /// larger number replays more after a crash and writes less during normal running.
    /// </param>
    public static ActorStream<T> Resume<T>(
        this ActorStream<T> source,
        IStreamPositions positions,
        string name,
        Func<T, long>? offsetOf = null,
        int checkpointEvery = 1)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(checkpointEvery, 1);

        return ActorStream<T>.From(Resumed(source, positions, name, offsetOf, checkpointEvery));
    }

    private static async IAsyncEnumerable<T> Resumed<T>(
        ActorStream<T> source,
        IStreamPositions positions,
        string name,
        Func<T, long>? offsetOf,
        int checkpointEvery,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var resumeFrom = await positions.LoadAsync(name, cancellationToken).ConfigureAwait(false);

        var counted = 0L;
        var handled = 0;
        var latest = resumeFrom;

        try
        {
            await foreach (var item in source.AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                var offset = offsetOf is null ? ++counted : offsetOf(item);

                // Skipped rather than not read: the source is replaying from its own beginning, and
                // only it knows how to start anywhere else.
                if (offset <= resumeFrom) continue;

                yield return item;

                // After the item has been yielded, so an interruption replays it rather than losing
                // it. The consumer has had it by the time control comes back here.
                latest = offset;
                if (++handled % checkpointEvery == 0)
                    await positions.SaveAsync(name, latest, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // The last partial batch, on every way out - a clean end, a consumer that stopped early,
            // an exception. Only a completed run reaches the end of the loop, and without this the
            // other two would replay everything since the last checkpoint for no reason.
            //
            // Not cancellable: this records work that has already happened, and a cancelled token
            // is the most likely reason to be here.
            if (handled % checkpointEvery != 0)
                await positions.SaveAsync(name, latest, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
