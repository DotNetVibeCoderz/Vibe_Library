// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Persistence;

/// <summary>A stored value together with the version it was read at.</summary>
public readonly record struct StoredState<T>(T Value, long Version);

/// <summary>One appended event.</summary>
public sealed record JournalEntry(string PersistenceId, long Sequence, object Event, DateTimeOffset Timestamp);

/// <summary>A snapshot of an event-sourced actor's state at a sequence number.</summary>
public sealed record StateSnapshot<T>(T State, long Sequence, DateTimeOffset Timestamp);

/// <summary>
/// Thrown when a write's expected version does not match what is stored - two writers raced for
/// the same key.
/// </summary>
/// <remarks>
/// The runtime keeps one activation per key per cluster, so this should not happen in normal
/// operation. It shows up during the brief overlap while an actor hands off between nodes, and
/// surfacing it is better than silently letting the loser overwrite the winner.
/// </remarks>
public sealed class StateConcurrencyException(string key, long expected, long actual) : ActorNetException(
    $"Concurrent write to '{key}': expected version {expected} but the store is at {actual}. Another activation of this actor wrote first.")
{
    public string Key { get; } = key;
    public long ExpectedVersion { get; } = expected;
    public long ActualVersion { get; } = actual;
}

/// <summary>
/// Key/value state storage for <see cref="PersistentActor{TState}"/>.
/// </summary>
/// <remarks>
/// The seam a database provider plugs into. Implementations only need three operations, and
/// versioning is optional in the sense that a store may always accept
/// <see cref="AnyVersion"/> - but a store that ignores versions cannot detect a split-brain
/// write.
/// </remarks>
public interface IStateStore
{
    /// <summary>Version value that means "write regardless of what is there".</summary>
    public const long AnyVersion = -1;

    /// <summary>Reads a key, or null when nothing has been written under it.</summary>
    Task<StoredState<T>?> ReadAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>Writes a key and returns the new version.</summary>
    /// <param name="expectedVersion">
    /// The version the caller last read, or <see cref="AnyVersion"/>. A mismatch throws
    /// <see cref="StateConcurrencyException"/>.
    /// </param>
    Task<long> WriteAsync<T>(string key, T state, long expectedVersion = AnyVersion, CancellationToken cancellationToken = default);

    /// <summary>Removes a key. Succeeds whether or not it existed.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// An append-only event log, one stream per persistence id.
/// </summary>
/// <remarks>
/// This is the CQRS half of the persistence story: state is a fold over the stream rather than a
/// row that gets overwritten, so history is replayable and a projection can be rebuilt from
/// scratch.
/// </remarks>
public interface IEventJournal
{
    /// <summary>Appends events and returns the sequence number of the last one.</summary>
    /// <param name="expectedSequence">
    /// The highest sequence the caller has seen, or <see cref="IStateStore.AnyVersion"/>.
    /// Mismatches throw <see cref="StateConcurrencyException"/>, which is what stops two
    /// activations of the same actor from interleaving events.
    /// </param>
    Task<long> AppendAsync(string persistenceId, IReadOnlyList<object> events, long expectedSequence = IStateStore.AnyVersion, CancellationToken cancellationToken = default);

    /// <summary>Reads a stream forward from <paramref name="fromSequence"/>, exclusive.</summary>
    IAsyncEnumerable<JournalEntry> ReadAsync(string persistenceId, long fromSequence = 0, CancellationToken cancellationToken = default);

    /// <summary>The highest sequence in a stream, or zero when it is empty.</summary>
    Task<long> HighestSequenceAsync(string persistenceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops events up to and including <paramref name="toSequence"/>. Only safe once a snapshot
    /// at or after that sequence exists.
    /// </summary>
    Task DeleteToAsync(string persistenceId, long toSequence, CancellationToken cancellationToken = default);
}

/// <summary>Snapshot storage, so recovery does not have to replay a stream from the beginning.</summary>
public interface ISnapshotStore
{
    /// <summary>Loads the newest snapshot, or null when there is none.</summary>
    Task<StateSnapshot<T>?> LoadAsync<T>(string persistenceId, CancellationToken cancellationToken = default);

    /// <summary>Stores a snapshot taken at <paramref name="sequence"/>.</summary>
    Task SaveAsync<T>(string persistenceId, T state, long sequence, CancellationToken cancellationToken = default);

    /// <summary>Removes the snapshot for a persistence id.</summary>
    Task DeleteAsync(string persistenceId, CancellationToken cancellationToken = default);
}

/// <summary>The three persistence seams, as an actor sees them.</summary>
public interface IPersistence
{
    /// <summary>Where <see cref="PersistentActor{TState}"/> keeps state.</summary>
    IStateStore State { get; }

    /// <summary>Where <see cref="EventSourcedActor{TState}"/> appends events.</summary>
    IEventJournal Journal { get; }

    /// <summary>Where event-sourced actors keep snapshots.</summary>
    ISnapshotStore Snapshots { get; }
}
