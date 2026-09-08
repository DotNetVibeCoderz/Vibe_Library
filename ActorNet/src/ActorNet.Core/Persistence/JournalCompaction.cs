// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Persistence;

/// <summary>
/// How much history to keep behind a snapshot.
/// </summary>
/// <param name="KeepEvents">
/// Events to keep after the snapshot's sequence, in addition to the snapshot itself. Zero deletes
/// everything the snapshot covers.
/// </param>
/// <param name="KeepFor">
/// The youngest events to keep regardless of <paramref name="KeepEvents"/>, or null for no such
/// floor.
/// </param>
/// <remarks>
/// <para>
/// A snapshot makes the events before it unnecessary for recovery, which is not the same as making
/// them unwanted. They are the audit trail, the input to a projection that has not caught up, and
/// the only way to answer a question about what happened that nobody thought to ask at the time.
/// </para>
/// <para>
/// So compaction takes a policy rather than a sequence number. <see cref="Aggressive"/> is the
/// behaviour <c>DeleteToAsync</c> already gave; the others keep a tail.
/// </para>
/// </remarks>
public sealed record CompactionPolicy(long KeepEvents = 0, TimeSpan? KeepFor = null)
{
    /// <summary>Keep nothing the snapshot covers. The smallest journal, and no history.</summary>
    public static CompactionPolicy Aggressive { get; } = new();

    /// <summary>Keep the last thousand events, whatever the snapshot covers.</summary>
    public static CompactionPolicy KeepRecent { get; } = new(KeepEvents: 1000);

    /// <summary>Keep a week of history behind the snapshot.</summary>
    public static CompactionPolicy KeepAWeek { get; } = new(KeepFor: TimeSpan.FromDays(7));
}

/// <summary>What a compaction did, or did not do.</summary>
/// <param name="PersistenceId">The stream.</param>
/// <param name="DeletedTo">The sequence deleted up to and including. Zero when nothing was deleted.</param>
/// <param name="Reason">Why nothing was deleted, when nothing was.</param>
public sealed record CompactionResult(string PersistenceId, long DeletedTo, string? Reason = null)
{
    /// <summary>Whether any events were removed.</summary>
    public bool Compacted => DeletedTo > 0;
}

/// <summary>
/// Truncates a journal safely: never past what a snapshot covers, and never past a retention policy.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IEventJournal.DeleteToAsync"/> deletes what it is told to delete. It is the primitive,
/// and it has no way to know whether a snapshot exists at that sequence - so getting the number
/// wrong loses state, silently, and the loss only surfaces the next time that actor recovers, which
/// can be weeks later on a node nobody was watching.
/// </para>
/// <para>
/// This checks first. It reads the snapshot, refuses to delete anything the snapshot does not cover,
/// and applies a retention policy on top. It cannot make the pair atomic - the two stores are
/// separate, and the journal has no transaction spanning them - but it fails in the safe direction:
/// an interruption between the check and the delete leaves more history than needed, never less.
/// </para>
/// </remarks>
public sealed class JournalCompactor(IEventJournal journal, ISnapshotStore snapshots)
{
    /// <summary>
    /// Compacts one stream, using the snapshot already stored for it.
    /// </summary>
    /// <typeparam name="TState">The snapshot's state type, needed to read it back.</typeparam>
    /// <param name="persistenceId">The stream to compact.</param>
    /// <param name="policy">How much to keep. Defaults to <see cref="CompactionPolicy.Aggressive"/>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<CompactionResult> CompactAsync<TState>(
        string persistenceId, CompactionPolicy? policy = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);
        policy ??= CompactionPolicy.Aggressive;

        var snapshot = await snapshots.LoadAsync<TState>(persistenceId, cancellationToken).ConfigureAwait(false);

        // No snapshot means every event is still needed to recover, so there is nothing safe to
        // delete. This is the case that loses an actor's state when it is got wrong.
        if (snapshot is null)
            return new CompactionResult(persistenceId, 0, "There is no snapshot, so every event is still needed to recover this actor.");

        var ceiling = snapshot.Sequence;
        if (ceiling <= 0)
            return new CompactionResult(persistenceId, 0, "The snapshot covers no events.");

        if (policy.KeepEvents > 0)
        {
            var highest = await journal.HighestSequenceAsync(persistenceId, cancellationToken).ConfigureAwait(false);

            // Counted back from the end of the stream rather than from the snapshot: the policy is
            // "keep the last N", and a snapshot taken long ago should not shrink that window.
            var floor = highest - policy.KeepEvents;
            if (floor < ceiling) ceiling = floor;
        }

        if (ceiling > 0 && policy.KeepFor is { } window)
        {
            var cutoff = DateTimeOffset.UtcNow - window;
            var youngest = 0L;

            // The last event older than the cutoff. Read forward because that is the only direction
            // the journal offers, and stop at the ceiling because nothing above it can be deleted.
            await foreach (var entry in journal.ReadAsync(persistenceId, 0, cancellationToken).ConfigureAwait(false))
            {
                if (entry.Sequence > ceiling) break;
                if (entry.Timestamp >= cutoff) break;
                youngest = entry.Sequence;
            }

            ceiling = youngest;
        }

        if (ceiling <= 0)
            return new CompactionResult(persistenceId, 0, "The retention policy keeps everything currently in the journal.");

        await journal.DeleteToAsync(persistenceId, ceiling, cancellationToken).ConfigureAwait(false);
        return new CompactionResult(persistenceId, ceiling);
    }

    /// <summary>
    /// Writes a snapshot and then compacts, which is the pair most callers want.
    /// </summary>
    /// <remarks>
    /// In this order, always. Truncating before the snapshot is stored is the one sequence that can
    /// lose an actor's state outright, and it is an easy thing to write by accident.
    /// </remarks>
    public async Task<CompactionResult> SnapshotAndCompactAsync<TState>(
        string persistenceId,
        TState state,
        long sequence,
        CompactionPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);

        await snapshots.SaveAsync(persistenceId, state, sequence, cancellationToken).ConfigureAwait(false);
        return await CompactAsync<TState>(persistenceId, policy, cancellationToken).ConfigureAwait(false);
    }
}
