// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ActorNet.Persistence;

/// <summary>
/// State storage as one JSON file per key, under a directory.
/// </summary>
/// <remarks>
/// <para>
/// The point of this store is that it survives a process restart without asking anyone to run a
/// database - which is what the samples and the CLI demos need to show a virtual actor genuinely
/// reloading its state. It is not a substitute for a real provider under load: every write is a
/// file write, and there is no transaction across keys.
/// </para>
/// <para>
/// Writes go to a temporary file and are then moved into place. A half-written JSON file is
/// unrecoverable state loss, and a move is the closest thing to atomic that a filesystem offers.
/// </para>
/// </remarks>
public sealed class FileStateStore : IStateStore
{
    private readonly string _root;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, IncludeFields = true };

    /// <summary>Creates a store rooted at <paramref name="directory"/>, creating it if needed.</summary>
    public FileStateStore(string directory)
    {
        _root = Directory.CreateDirectory(directory).FullName;
    }

    /// <inheritdoc />
    public async Task<StoredState<T>?> ReadAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) return null;

        var gate = GateFor(key);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = File.OpenRead(path);
            var envelope = await JsonSerializer.DeserializeAsync<Entry<T>>(stream, _json, cancellationToken).ConfigureAwait(false);
            return envelope is null ? null : new StoredState<T>(envelope.Value, envelope.Version);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<long> WriteAsync<T>(string key, T state, long expectedVersion = IStateStore.AnyVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var path = PathFor(key);
        var gate = GateFor(key);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = 0L;
            if (File.Exists(path))
            {
                await using var read = File.OpenRead(path);
                var existing = await JsonSerializer.DeserializeAsync<Entry<T>>(read, _json, cancellationToken).ConfigureAwait(false);
                current = existing?.Version ?? 0;
            }

            if (expectedVersion != IStateStore.AnyVersion && expectedVersion != current)
                throw new StateConcurrencyException(key, expectedVersion, current);

            var next = current + 1;
            var temp = path + ".tmp";
            await using (var write = File.Create(temp))
                await JsonSerializer.SerializeAsync(write, new Entry<T>(state, next, key), _json, cancellationToken).ConfigureAwait(false);

            File.Move(temp, path, overwrite: true);
            return next;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private SemaphoreSlim GateFor(string key) => _gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));

    /// <summary>
    /// Maps a key to a filename.
    /// </summary>
    /// <remarks>
    /// Keys are user data and contain <c>/</c> by construction, so they are hashed rather than
    /// used as paths - both to keep the key's own separators from creating directories and to stop
    /// a key like <c>../../etc/passwd</c> from escaping the store's root.
    /// </remarks>
    private string PathFor(string key)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
        return Path.Combine(_root, $"{Sanitize(key)}-{hash}.json");
    }

    private static string Sanitize(string key)
    {
        var builder = new StringBuilder(Math.Min(key.Length, 48));
        foreach (var c in key.Take(48))
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        return builder.ToString();
    }

    private sealed record Entry<T>(T Value, long Version, string Key);
}

/// <summary>
/// An event journal as one append-only JSON-lines file per stream.
/// </summary>
/// <remarks>
/// JSON Lines rather than a JSON array, because appending to an array means rewriting the file:
/// with one object per line an append is a seek to the end and a write, which is what an
/// append-only log should cost.
/// </remarks>
public sealed class FileEventJournal : IEventJournal
{
    private readonly string _root;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly Serialization.MessageTypeRegistry _types;
    private readonly JsonSerializerOptions _json = new() { IncludeFields = true };

    /// <summary>
    /// Creates a journal rooted at <paramref name="directory"/>.
    /// </summary>
    /// <param name="types">
    /// The allow-list used to resolve event types on replay. Sharing the actor system's registry
    /// means an event type registered for the wire is also readable from disk.
    /// </param>
    public FileEventJournal(string directory, Serialization.MessageTypeRegistry? types = null)
    {
        _root = Directory.CreateDirectory(directory).FullName;
        _types = types ?? new Serialization.MessageTypeRegistry();
    }

    /// <inheritdoc />
    public async Task<long> AppendAsync(string persistenceId, IReadOnlyList<object> events, long expectedSequence = IStateStore.AnyVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0) return await HighestSequenceAsync(persistenceId, cancellationToken).ConfigureAwait(false);

        var gate = GateFor(persistenceId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await ReadHighestAsync(persistenceId, cancellationToken).ConfigureAwait(false);
            if (expectedSequence != IStateStore.AnyVersion && expectedSequence != current)
                throw new StateConcurrencyException(persistenceId, expectedSequence, current);

            var lines = new StringBuilder();
            var now = DateTimeOffset.UtcNow;
            foreach (var e in events)
            {
                var alias = _types.AliasOf(e.GetType());
                var record = new Line(++current, alias, JsonSerializer.SerializeToElement(e, e.GetType(), _json), now);
                lines.AppendLine(JsonSerializer.Serialize(record, _json));
            }

            await File.AppendAllTextAsync(PathFor(persistenceId), lines.ToString(), cancellationToken).ConfigureAwait(false);
            return current;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<JournalEntry> ReadAsync(string persistenceId, long fromSequence = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var path = PathFor(persistenceId);
        if (!File.Exists(path)) yield break;

        await foreach (var raw in File.ReadLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var line = JsonSerializer.Deserialize<Line>(raw, _json);
            if (line is null || line.Sequence <= fromSequence) continue;

            var type = _types.Resolve(line.Alias);
            var payload = JsonSerializer.Deserialize(line.Payload, type, _json)
                          ?? throw new ActorNetException($"Event {line.Sequence} in stream '{persistenceId}' deserialized to null.");

            yield return new JournalEntry(persistenceId, line.Sequence, payload, line.Timestamp);
        }
    }

    /// <inheritdoc />
    public async Task<long> HighestSequenceAsync(string persistenceId, CancellationToken cancellationToken = default)
    {
        var gate = GateFor(persistenceId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ReadHighestAsync(persistenceId, cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    /// <inheritdoc />
    public async Task DeleteToAsync(string persistenceId, long toSequence, CancellationToken cancellationToken = default)
    {
        var path = PathFor(persistenceId);
        if (!File.Exists(path)) return;

        var gate = GateFor(persistenceId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var kept = new List<string>();
            foreach (var raw in await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var line = JsonSerializer.Deserialize<Line>(raw, _json);
                if (line is not null && line.Sequence > toSequence) kept.Add(raw);
            }

            var temp = path + ".tmp";
            await File.WriteAllLinesAsync(temp, kept, cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<long> ReadHighestAsync(string persistenceId, CancellationToken cancellationToken)
    {
        var path = PathFor(persistenceId);
        if (!File.Exists(path)) return 0;

        var highest = 0L;
        await foreach (var raw in File.ReadLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var line = JsonSerializer.Deserialize<Line>(raw, _json);
            if (line is not null && line.Sequence > highest) highest = line.Sequence;
        }

        return highest;
    }

    private SemaphoreSlim GateFor(string id) => _gates.GetOrAdd(id, static _ => new SemaphoreSlim(1, 1));

    private string PathFor(string persistenceId)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(persistenceId)))[..32];
        var safe = new string(persistenceId.Take(48).Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return Path.Combine(_root, $"{safe}-{hash}.jsonl");
    }

    private sealed record Line(long Sequence, string Alias, JsonElement Payload, DateTimeOffset Timestamp);
}

/// <summary>Snapshots as one JSON file per persistence id, alongside a <see cref="FileEventJournal"/>.</summary>
public sealed class FileSnapshotStore(string directory) : ISnapshotStore
{
    private readonly FileStateStore _inner = new(directory);

    /// <inheritdoc />
    public async Task<StateSnapshot<T>?> LoadAsync<T>(string persistenceId, CancellationToken cancellationToken = default)
    {
        var stored = await _inner.ReadAsync<Snapshot<T>>(persistenceId, cancellationToken).ConfigureAwait(false);
        return stored is null ? null : new StateSnapshot<T>(stored.Value.Value.State, stored.Value.Value.Sequence, stored.Value.Value.Timestamp);
    }

    /// <inheritdoc />
    public Task SaveAsync<T>(string persistenceId, T state, long sequence, CancellationToken cancellationToken = default) =>
        _inner.WriteAsync(persistenceId, new Snapshot<T>(state, sequence, DateTimeOffset.UtcNow), IStateStore.AnyVersion, cancellationToken);

    /// <inheritdoc />
    public Task DeleteAsync(string persistenceId, CancellationToken cancellationToken = default) =>
        _inner.DeleteAsync(persistenceId, cancellationToken);

    private sealed record Snapshot<T>(T State, long Sequence, DateTimeOffset Timestamp);
}
