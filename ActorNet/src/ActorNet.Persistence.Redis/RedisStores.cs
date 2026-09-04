// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.CompilerServices;
using System.Text.Json;
using ActorNet.Serialization;
using StackExchange.Redis;

namespace ActorNet.Persistence.Redis;

/// <summary>Key naming and behaviour shared by the Redis stores.</summary>
public sealed class RedisStoreOptions
{
    /// <summary>
    /// Prefix for every key this library writes, so it can share a Redis instance with other
    /// applications - and so a stray <c>KEYS actornet:*</c> shows exactly what belongs to it.
    /// </summary>
    public string KeyPrefix { get; set; } = "actornet:";

    /// <summary>Which logical database to use.</summary>
    public int Database { get; set; } = -1;

    internal string StateKey(string key) => $"{KeyPrefix}state:{key}";
    internal string EventKey(string id) => $"{KeyPrefix}events:{id}";
    internal string SnapshotKey(string id) => $"{KeyPrefix}snapshot:{id}";
}

/// <summary>
/// State storage in Redis, one hash per actor.
/// </summary>
/// <remarks>
/// <para>
/// The version check and the write have to be one indivisible step, or two activations of the same
/// actor can both read version 4 and both write version 5. Redis has no compare-and-set across
/// fields, so the write is a Lua script: Redis runs a script to completion without interleaving
/// another command, which is exactly the guarantee needed and is cheaper than <c>WATCH</c>/
/// <c>MULTI</c> with a retry loop.
/// </para>
/// <para>
/// A caveat worth stating plainly: Redis persistence is configurable and often off. With the
/// default RDB snapshotting, a crash loses the last few seconds of writes. That is fine for a cache
/// and not fine for a ledger - use a relational provider when losing a write is unacceptable.
/// </para>
/// </remarks>
public sealed class RedisStateStore : IStateStore
{
    // KEYS[1] state hash, ARGV[1] payload, ARGV[2] expected version (-1 = any), ARGV[3] timestamp.
    // Returns the new version, or -1 when the expected version did not match.
    private const string WriteScript = """
        local current = tonumber(redis.call('HGET', KEYS[1], 'version')) or 0
        local expected = tonumber(ARGV[2])
        if expected ~= -1 and expected ~= current then
            return -1
        end
        local next_version = current + 1
        redis.call('HSET', KEYS[1], 'version', next_version, 'payload', ARGV[1], 'updated_at', ARGV[3])
        return next_version
        """;

    private readonly IConnectionMultiplexer _connection;
    private readonly RedisStoreOptions _options;
    private readonly JsonSerializerOptions _json = new() { IncludeFields = true };

    public RedisStateStore(IConnectionMultiplexer connection, RedisStoreOptions? options = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options ?? new RedisStoreOptions();
    }

    private IDatabase Db => _connection.GetDatabase(_options.Database);

    /// <inheritdoc />
    public async Task<StoredState<T>?> ReadAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var values = await Db.HashGetAsync(_options.StateKey(key), ["version", "payload"]).ConfigureAwait(false);
        if (values[0].IsNull || values[1].IsNull) return null;

        var value = JsonSerializer.Deserialize<T>((string)values[1]!, _json)
                    ?? throw new ActorNetException($"State for '{key}' deserialized to null.");

        return new StoredState<T>(value, (long)values[0]);
    }

    /// <inheritdoc />
    public async Task<long> WriteAsync<T>(string key, T state, long expectedVersion = IStateStore.AnyVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(state);

        var payload = JsonSerializer.Serialize(state, _json);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var result = (long)await Db.ScriptEvaluateAsync(
            WriteScript,
            [_options.StateKey(key)],
            [payload, expectedVersion, now]).ConfigureAwait(false);

        if (result >= 0) return result;

        // The script refused, so re-read to report what the version actually is. Racy in principle
        // - it may have moved again - but the caller only needs to know its own write was stale.
        var actual = await Db.HashGetAsync(_options.StateKey(key), "version").ConfigureAwait(false);
        throw new StateConcurrencyException(key, expectedVersion, actual.IsNull ? 0 : (long)actual);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Db.KeyDeleteAsync(_options.StateKey(key));
    }
}

/// <summary>
/// An append-only event journal in Redis, one sorted set per stream.
/// </summary>
/// <remarks>
/// A sorted set scored by sequence number rather than a list, because the journal needs three
/// things a list cannot do well: read forward from an arbitrary sequence, find the tip in constant
/// time, and drop everything up to a snapshot. <c>ZRANGEBYSCORE</c>, <c>ZRANGE -1</c> and
/// <c>ZREMRANGEBYSCORE</c> are each one command.
/// </remarks>
public sealed class RedisEventJournal : IEventJournal
{
    // KEYS[1] stream, ARGV[1] expected sequence (-1 = any), ARGV[2..] the encoded events.
    // Returns the new tip, or -1 when the expected sequence did not match.
    private const string AppendScript = """
        local tip = 0
        local last = redis.call('ZRANGE', KEYS[1], -1, -1, 'WITHSCORES')
        if #last > 0 then
            tip = tonumber(last[2])
        end
        local expected = tonumber(ARGV[1])
        if expected ~= -1 and expected ~= tip then
            return -1
        end
        for i = 2, #ARGV do
            tip = tip + 1
            redis.call('ZADD', KEYS[1], tip, tip .. ':' .. ARGV[i])
        end
        return tip
        """;

    private readonly IConnectionMultiplexer _connection;
    private readonly RedisStoreOptions _options;
    private readonly MessageTypeRegistry _types;
    private readonly JsonSerializerOptions _json = new() { IncludeFields = true };

    public RedisEventJournal(IConnectionMultiplexer connection, MessageTypeRegistry? types = null, RedisStoreOptions? options = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _types = types ?? new MessageTypeRegistry();
        _options = options ?? new RedisStoreOptions();
    }

    private IDatabase Db => _connection.GetDatabase(_options.Database);

    /// <inheritdoc />
    public async Task<long> AppendAsync(
        string persistenceId, IReadOnlyList<object> events, long expectedSequence = IStateStore.AnyVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0) return await HighestSequenceAsync(persistenceId, cancellationToken).ConfigureAwait(false);

        // Each member is "sequence:alias:payload". The sequence prefix is what keeps two identical
        // events distinct - a sorted set stores unique members, so appending the same event twice
        // would otherwise silently update one entry instead of adding a second.
        var arguments = new RedisValue[events.Count + 1];
        arguments[0] = expectedSequence;

        for (var i = 0; i < events.Count; i++)
        {
            var alias = _types.AliasOf(events[i].GetType());
            var payload = JsonSerializer.Serialize(events[i], events[i].GetType(), _json);
            arguments[i + 1] = $"{alias}{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{payload}";
        }

        var tip = (long)await Db.ScriptEvaluateAsync(
            AppendScript, [_options.EventKey(persistenceId)], arguments).ConfigureAwait(false);

        if (tip >= 0) return tip;

        var actual = await HighestSequenceAsync(persistenceId, cancellationToken).ConfigureAwait(false);
        throw new StateConcurrencyException(persistenceId, expectedSequence, actual);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<JournalEntry> ReadAsync(
        string persistenceId, long fromSequence = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);

        var entries = await Db.SortedSetRangeByScoreAsync(
            _options.EventKey(persistenceId),
            start: fromSequence,
            exclude: Exclude.Start).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // "sequence:aliastimestamppayload" - the colon splits off the sequence the
            // Lua script prefixed, then unit separators split the rest. A unit separator is used
            // because it cannot occur in JSON or in a type alias.
            var raw = (string)entry!;
            var colon = raw.IndexOf(':');
            if (colon <= 0) continue;

            var sequence = long.Parse(raw[..colon]);
            var parts = raw[(colon + 1)..].Split('', 3);
            if (parts.Length != 3) continue;

            var type = _types.Resolve(parts[0]);
            var at = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(parts[1]));
            var domainEvent = JsonSerializer.Deserialize(parts[2], type, _json)
                              ?? throw new ActorNetException($"Event {sequence} in stream '{persistenceId}' deserialized to null.");

            yield return new JournalEntry(persistenceId, sequence, domainEvent, at);
        }
    }

    /// <inheritdoc />
    public async Task<long> HighestSequenceAsync(string persistenceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);

        var last = await Db.SortedSetRangeByRankWithScoresAsync(_options.EventKey(persistenceId), -1, -1).ConfigureAwait(false);
        return last.Length == 0 ? 0 : (long)last[0].Score;
    }

    /// <inheritdoc />
    public Task DeleteToAsync(string persistenceId, long toSequence, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);
        return Db.SortedSetRemoveRangeByScoreAsync(_options.EventKey(persistenceId), double.NegativeInfinity, toSequence);
    }
}

/// <summary>Snapshot storage in Redis, one key per persistence id.</summary>
/// <remarks>
/// Last write wins, with no version check - a snapshot is an optimization over replay, so losing a
/// race here costs a slower recovery rather than a wrong answer.
/// </remarks>
public sealed class RedisSnapshotStore : ISnapshotStore
{
    private readonly IConnectionMultiplexer _connection;
    private readonly RedisStoreOptions _options;
    private readonly JsonSerializerOptions _json = new() { IncludeFields = true };

    public RedisSnapshotStore(IConnectionMultiplexer connection, RedisStoreOptions? options = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options ?? new RedisStoreOptions();
    }

    private IDatabase Db => _connection.GetDatabase(_options.Database);

    /// <inheritdoc />
    public async Task<StateSnapshot<T>?> LoadAsync<T>(string persistenceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);

        var values = await Db.HashGetAsync(_options.SnapshotKey(persistenceId), ["seq", "payload", "at"]).ConfigureAwait(false);
        if (values[0].IsNull || values[1].IsNull) return null;

        var state = JsonSerializer.Deserialize<T>((string)values[1]!, _json)
                    ?? throw new ActorNetException($"Snapshot for '{persistenceId}' deserialized to null.");

        return new StateSnapshot<T>(state, (long)values[0], DateTimeOffset.FromUnixTimeMilliseconds((long)values[2]));
    }

    /// <inheritdoc />
    public Task SaveAsync<T>(string persistenceId, T state, long sequence, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);
        ArgumentNullException.ThrowIfNull(state);

        return Db.HashSetAsync(_options.SnapshotKey(persistenceId),
        [
            new HashEntry("seq", sequence),
            new HashEntry("payload", JsonSerializer.Serialize(state, _json)),
            new HashEntry("at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
        ]);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string persistenceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);
        return Db.KeyDeleteAsync(_options.SnapshotKey(persistenceId));
    }
}

/// <summary>Builds Redis-backed stores.</summary>
public static class RedisPersistence
{
    /// <summary>State storage in Redis.</summary>
    public static IStateStore StateStore(IConnectionMultiplexer connection, RedisStoreOptions? options = null) =>
        new RedisStateStore(connection, options);

    /// <summary>An event journal in Redis.</summary>
    public static IEventJournal EventJournal(IConnectionMultiplexer connection, MessageTypeRegistry? types = null, RedisStoreOptions? options = null) =>
        new RedisEventJournal(connection, types, options);

    /// <summary>Snapshot storage in Redis.</summary>
    public static ISnapshotStore SnapshotStore(IConnectionMultiplexer connection, RedisStoreOptions? options = null) =>
        new RedisSnapshotStore(connection, options);

    /// <summary>Points all three stores at one Redis connection.</summary>
    /// <remarks>
    /// The connection is not owned by the stores and is not disposed with the actor system:
    /// <see cref="IConnectionMultiplexer"/> is designed to be shared for the life of the
    /// application, and an application usually has other things using it.
    /// </remarks>
    public static ActorSystemOptions UseRedis(
        this ActorSystemOptions options, IConnectionMultiplexer connection, MessageTypeRegistry? types = null, RedisStoreOptions? storeOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.StateStore = StateStore(connection, storeOptions);
        options.EventJournal = EventJournal(connection, types, storeOptions);
        options.SnapshotStore = SnapshotStore(connection, storeOptions);
        return options;
    }
}
