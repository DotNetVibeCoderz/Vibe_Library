// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace ActorNet.Persistence;

/// <summary>
/// State storage in a dictionary. Durable across deactivation and reactivation, which is what
/// makes the virtual-actor lifecycle work, but not across a process restart.
/// </summary>
/// <remarks>
/// The default for a reason: it makes the framework work out of the box and it makes tests fast.
/// It is the wrong choice the moment the state matters - use <see cref="FileStateStore"/> or a
/// database provider then.
/// </remarks>
public sealed class InMemoryStateStore : IStateStore
{
    private readonly ConcurrentDictionary<string, (object Value, long Version)> _entries = new(StringComparer.Ordinal);

    /// <summary>How many keys are stored. Useful in tests and on the dashboard.</summary>
    public int Count => _entries.Count;

    /// <inheritdoc />
    public Task<StoredState<T>?> ReadAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.TryGetValue(key, out var entry) && entry.Value is T typed
            ? new StoredState<T>(StateCloning.Clone(typed), entry.Version)
            : (StoredState<T>?)null);

    /// <inheritdoc />
    public Task<long> WriteAsync<T>(string key, T state, long expectedVersion = IStateStore.AnyVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Cloned once, before the retry loop: the caller keeps mutating its own instance, and a
        // store that held a reference to it would keep changing after the write returned.
        var stored = StateCloning.Clone(state);

        while (true)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                if (expectedVersion != IStateStore.AnyVersion && expectedVersion != existing.Version)
                    throw new StateConcurrencyException(key, expectedVersion, existing.Version);

                var next = (Value: (object)stored!, Version: existing.Version + 1);
                if (_entries.TryUpdate(key, next, existing)) return Task.FromResult(next.Version);
                continue;
            }

            if (expectedVersion > 0) throw new StateConcurrencyException(key, expectedVersion, 0);
            if (_entries.TryAdd(key, (stored!, 1))) return Task.FromResult(1L);
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

/// <summary>An event journal in memory. Same trade-offs as <see cref="InMemoryStateStore"/>.</summary>
public sealed class InMemoryEventJournal : IEventJournal
{
    private readonly ConcurrentDictionary<string, List<JournalEntry>> _streams = new(StringComparer.Ordinal);

    /// <summary>Persistence ids that have at least one event.</summary>
    public IReadOnlyCollection<string> Streams => (IReadOnlyCollection<string>)_streams.Keys;

    /// <inheritdoc />
    public Task<long> AppendAsync(string persistenceId, IReadOnlyList<object> events, long expectedSequence = IStateStore.AnyVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        var stream = _streams.GetOrAdd(persistenceId, static _ => []);

        // The list is the lock. Appends to one stream are rare enough that contention does not
        // matter, and ordering within a stream is the one thing a journal cannot get wrong.
        lock (stream)
        {
            var current = stream.Count == 0 ? 0 : stream[^1].Sequence;
            if (expectedSequence != IStateStore.AnyVersion && expectedSequence != current)
                throw new StateConcurrencyException(persistenceId, expectedSequence, current);

            var now = DateTimeOffset.UtcNow;
            foreach (var e in events) stream.Add(new JournalEntry(persistenceId, ++current, e, now));
            return Task.FromResult(current);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<JournalEntry> ReadAsync(string persistenceId, long fromSequence = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_streams.TryGetValue(persistenceId, out var stream)) yield break;

        JournalEntry[] snapshot;
        lock (stream) snapshot = stream.Where(e => e.Sequence > fromSequence).ToArray();

        foreach (var entry in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<long> HighestSequenceAsync(string persistenceId, CancellationToken cancellationToken = default)
    {
        if (!_streams.TryGetValue(persistenceId, out var stream)) return Task.FromResult(0L);
        lock (stream) return Task.FromResult(stream.Count == 0 ? 0 : stream[^1].Sequence);
    }

    /// <inheritdoc />
    public Task DeleteToAsync(string persistenceId, long toSequence, CancellationToken cancellationToken = default)
    {
        if (!_streams.TryGetValue(persistenceId, out var stream)) return Task.CompletedTask;
        lock (stream) stream.RemoveAll(e => e.Sequence <= toSequence);
        return Task.CompletedTask;
    }
}

/// <summary>A snapshot store in memory. Same trade-offs as <see cref="InMemoryStateStore"/>.</summary>
public sealed class InMemorySnapshotStore : ISnapshotStore
{
    private readonly ConcurrentDictionary<string, (object State, long Sequence, DateTimeOffset At)> _snapshots = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<StateSnapshot<T>?> LoadAsync<T>(string persistenceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_snapshots.TryGetValue(persistenceId, out var entry) && entry.State is T typed
            ? new StateSnapshot<T>(StateCloning.Clone(typed), entry.Sequence, entry.At)
            : null);

    /// <inheritdoc />
    public Task SaveAsync<T>(string persistenceId, T state, long sequence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        // The actor keeps folding events into this same instance after the snapshot is taken.
        // Storing the reference would leave a snapshot labelled with sequence N but holding the
        // state as of some later sequence, and recovery would double-apply everything between.
        _snapshots[persistenceId] = (StateCloning.Clone(state)!, sequence, DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string persistenceId, CancellationToken cancellationToken = default)
    {
        _snapshots.TryRemove(persistenceId, out _);
        return Task.CompletedTask;
    }
}
