// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Streams;

namespace ActorNet.Persistence;

/// <summary>
/// A read model built by folding a journal, kept up to date by being run again.
/// </summary>
/// <remarks>
/// The events are the record and this is a view of them, so it can always be thrown away and
/// rebuilt - which is the property that makes a projection worth having, and the reason
/// <see cref="ProjectionRunner"/> can be pointed at offset zero.
/// </remarks>
public interface IProjection
{
    /// <summary>The name its position is stored under. Changing it replays from the beginning.</summary>
    string Name { get; }

    /// <summary>Folds one event in.</summary>
    /// <remarks>
    /// Called at least once per event. The runner stores its position after handling rather than
    /// before, so an interruption replays rather than skips - which makes this the place that has
    /// to tolerate seeing the same event twice.
    /// </remarks>
    Task ApplyAsync(JournalEntry entry, CancellationToken cancellationToken);
}

/// <summary>
/// Runs a projection over one journal stream, from where it left off.
/// </summary>
/// <remarks>
/// <para>
/// One stream at a time, and that is the honest shape of it. A projection over <em>every</em> event
/// in the journal needs the journal to have a total order across streams, and
/// <see cref="IEventJournal"/> has no such thing: sequences are per persistence id, so there is no
/// position that means "everything before this, everywhere". Adding one means a column in every
/// provider's schema and a reader that tolerates the gaps an auto-increment leaves when two writers
/// commit out of order - a migration and a correctness problem, not an afternoon.
/// </para>
/// <para>
/// What this does cover is the common case it is usually wanted for: fold one aggregate's history
/// into a view, catch up after a restart, rebuild from scratch on demand. Several streams means
/// several runners, which is fine when the caller knows which streams it cares about.
/// </para>
/// </remarks>
public sealed class ProjectionRunner(IEventJournal journal, IStreamPositions positions)
{
    /// <summary>
    /// Folds everything not yet seen, and returns how many events were applied.
    /// </summary>
    /// <param name="projection">The read model to bring up to date.</param>
    /// <param name="persistenceId">The stream to fold.</param>
    /// <param name="checkpointEvery">
    /// Events applied between writes of the position. Larger replays more after a crash and writes
    /// less while running.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<long> CatchUpAsync(
        IProjection projection, string persistenceId, int checkpointEvery = 1, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);

        // Named per stream, so one projection folding several of them keeps a position for each
        // rather than one position that is wrong for all but the last.
        var name = $"{projection.Name}/{persistenceId}";

        var stream = ActorStream<JournalEntry>
            .From(journal.ReadAsync(persistenceId, 0, cancellationToken))
            .Resume(positions, name, static entry => entry.Sequence, checkpointEvery);

        return await stream
            .RunAsync((entry, token) => new ValueTask(projection.ApplyAsync(entry, token)), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Forgets where a projection had got to, so the next run folds the stream from the beginning.
    /// </summary>
    /// <remarks>
    /// The caller has to clear the read model itself. This only moves the position, and a runner
    /// that also emptied somebody else's table would be guessing at where it was.
    /// </remarks>
    public Task RewindAsync(IProjection projection, string persistenceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);

        return positions.SaveAsync($"{projection.Name}/{persistenceId}", 0, cancellationToken);
    }

    /// <summary>How far a projection has got through one stream.</summary>
    public Task<long> PositionAsync(IProjection projection, string persistenceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);

        return positions.LoadAsync($"{projection.Name}/{persistenceId}", cancellationToken);
    }
}
