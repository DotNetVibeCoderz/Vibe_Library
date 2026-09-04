// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Persistence;

/// <summary>
/// A virtual actor whose state is a fold over an append-only event stream.
/// </summary>
/// <remarks>
/// <para>
/// The Akka-style persistence model, and the CQRS half of the requirements. Instead of storing
/// what the state <em>is</em>, the actor stores what <em>happened</em>: a handler validates a
/// command and calls <see cref="PersistAsync"/>, the event is appended, and
/// <see cref="Apply"/> folds it into the state. Recovery replays the same folds, so there is
/// exactly one code path that can change state and history is never lost to an overwrite.
/// </para>
/// <para>
/// The ordering inside <see cref="PersistAsync"/> matters: the event is written <em>before</em>
/// it is applied. Applying first would let an actor acknowledge a change that the journal then
/// refused, which is the one failure an event-sourced actor must not have.
/// </para>
/// <para>
/// Snapshots are an optimization over replay, never a source of truth: recovery loads the newest
/// snapshot and replays only the events after it. Set <see cref="SnapshotEvery"/> once a stream
/// is long enough that replaying it is slower than reading a snapshot.
/// </para>
/// </remarks>
/// <typeparam name="TState">State shape. Must be JSON round-trippable with a parameterless constructor.</typeparam>
public abstract class EventSourcedActor<TState> : VirtualActor where TState : class, new()
{
    private IEventJournal _journal = null!;
    private ISnapshotStore _snapshots = null!;
    private long _sequence;
    private long _eventsSinceSnapshot;

    /// <summary>The folded state. Never null after activation.</summary>
    protected TState State { get; private set; } = new();

    /// <summary>Sequence number of the last event this actor has applied.</summary>
    protected long Sequence => _sequence;

    /// <summary>True while recovery is replaying history, false once the actor is live.</summary>
    /// <remarks>
    /// Check this before any side effect. Replay re-runs every <see cref="Apply"/> call the actor
    /// has ever made, so a charge or an email sent from inside a fold happens again on every
    /// activation.
    /// </remarks>
    protected bool IsRecovering { get; private set; }

    /// <summary>Take a snapshot every this many events. Zero disables snapshotting.</summary>
    protected virtual long SnapshotEvery => 0;

    /// <summary>
    /// Delete journal entries covered by a snapshot once it is written. Off by default: the whole
    /// point of a journal is often the audit trail, and a snapshot is not one.
    /// </summary>
    protected virtual bool TruncateOnSnapshot => false;

    /// <summary>The stream id. Defaults to the actor's address.</summary>
    protected virtual string PersistenceId => Context.Self.ToString();

    /// <summary>Folds one event into the state. Must be pure: it runs again on every recovery.</summary>
    protected abstract void Apply(object domainEvent);

    /// <summary>Recovers by loading the newest snapshot and replaying everything after it.</summary>
    protected override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        (_journal, _snapshots) = ResolveStores();

        State = new TState();
        _sequence = 0;
        IsRecovering = true;

        try
        {
            var snapshot = await _snapshots.LoadAsync<TState>(PersistenceId, cancellationToken).ConfigureAwait(false);
            if (snapshot is not null)
            {
                State = snapshot.State;
                _sequence = snapshot.Sequence;
            }

            var replayed = 0;
            await foreach (var entry in _journal.ReadAsync(PersistenceId, _sequence, cancellationToken).ConfigureAwait(false))
            {
                Apply(entry.Event);
                _sequence = entry.Sequence;
                replayed++;
            }

            _eventsSinceSnapshot = replayed;
            await OnRecoveryCompletedAsync(replayed, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            IsRecovering = false;
        }
    }

    /// <summary>Runs once recovery has finished, with the number of events replayed.</summary>
    protected virtual Task OnRecoveryCompletedAsync(int eventsReplayed, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Appends one event, then folds it in.</summary>
    protected Task PersistAsync(object domainEvent, CancellationToken cancellationToken = default) =>
        PersistAllAsync([domainEvent], cancellationToken);

    /// <summary>
    /// Appends several events as one journal write, then folds them in order.
    /// </summary>
    /// <remarks>
    /// Use this rather than several <see cref="PersistAsync"/> calls when a command produces
    /// events that only make sense together - the single append is what stops a crash from leaving
    /// half of them in the stream.
    /// </remarks>
    protected async Task PersistAllAsync(IReadOnlyList<object> domainEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);
        if (domainEvents.Count == 0) return;

        if (IsRecovering)
            throw new ActorNetException(
                $"{GetType().Name} tried to persist while recovering. Recovery replays history; it must not append to it.");

        _sequence = await _journal.AppendAsync(PersistenceId, domainEvents, _sequence, cancellationToken).ConfigureAwait(false);

        foreach (var e in domainEvents)
        {
            Apply(e);
            _eventsSinceSnapshot++;
        }

        if (SnapshotEvery > 0 && _eventsSinceSnapshot >= SnapshotEvery)
            await SaveSnapshotAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a snapshot at the current sequence.</summary>
    protected async Task SaveSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _snapshots.SaveAsync(PersistenceId, State, _sequence, cancellationToken).ConfigureAwait(false);
        _eventsSinceSnapshot = 0;

        if (TruncateOnSnapshot && _sequence > 0)
            await _journal.DeleteToAsync(PersistenceId, _sequence, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads this actor's history, oldest first. For projections and audit views.</summary>
    protected IAsyncEnumerable<JournalEntry> ReadHistoryAsync(long fromSequence = 0, CancellationToken cancellationToken = default) =>
        _journal.ReadAsync(PersistenceId, fromSequence, cancellationToken);

    private (IEventJournal Journal, ISnapshotStore Snapshots) ResolveStores() => Context.System is ActorSystem system
        ? (system.Options.EventJournal, system.Options.SnapshotStore)
        : throw new ActorNetException("EventSourcedActor requires the built-in ActorSystem, which owns the journal and snapshot store.");
}
