// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Data;
using System.Data.Common;
using System.Text.Json;

namespace ActorNet.Persistence.Relational;

/// <summary>
/// State storage in a relational table, one row per actor.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the in-memory and file stores, this one is shared between nodes - which is the whole
/// point. An actor whose key moves to another node during a rebalance is flushed here and reloaded
/// there, and until a store both nodes can read exists, that handoff loses the state.
/// </para>
/// <para>
/// Every write is a single statement whose <c>WHERE</c> clause carries the expected version, so
/// the version check and the write cannot be separated by another writer. No transaction is needed
/// for that, and none is taken.
/// </para>
/// </remarks>
public sealed class RelationalStateStore : IStateStore
{
    private readonly ISqlDialect _dialect;
    private readonly string _connectionString;
    private readonly RelationalStoreOptions _options;
    private readonly JsonSerializerOptions _json = new() { IncludeFields = true };
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private volatile bool _schemaReady;

    public RelationalStateStore(ISqlDialect dialect, string connectionString, RelationalStoreOptions? options = null)
    {
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _options = options ?? new RelationalStoreOptions();
        _options.Validate();
    }

    /// <inheritdoc />
    public async Task<StoredState<T>?> ReadAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT state_version, payload FROM {_options.StateTable} WHERE actor_key = @key";
        command.AddParameter("@key", key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        var version = reader.GetInt64(0);
        var payload = reader.GetString(1);
        var value = JsonSerializer.Deserialize<T>(payload, _json)
                    ?? throw new ActorNetException($"State for '{key}' deserialized to null.");

        return new StoredState<T>(value, version);
    }

    /// <inheritdoc />
    public async Task<long> WriteAsync<T>(string key, T state, long expectedVersion = IStateStore.AnyVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(state);

        if (key.Length > _options.MaxKeyLength)
            throw new ArgumentException(
                $"Actor key '{key}' is {key.Length} characters; the schema stores at most {_options.MaxKeyLength}.",
                nameof(key));

        var payload = JsonSerializer.Serialize(state, _json);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        // Update first, insert only if the row was not there. Doing it in that order means the
        // common case - an actor that has been persisted before - is one statement, and it avoids
        // needing a portable upsert, which is the one piece of DML the four databases genuinely
        // spell differently.
        var updated = await TryUpdateAsync(connection, key, payload, expectedVersion, now, cancellationToken).ConfigureAwait(false);
        if (updated is { } newVersion) return newVersion;

        if (expectedVersion > 0)
        {
            // The caller read a version, so a row existed then and does not now, or its version
            // moved. Either way this write is built on something stale.
            throw new StateConcurrencyException(key, expectedVersion, await ReadVersionAsync(connection, key, cancellationToken).ConfigureAwait(false));
        }

        try
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText =
                $"INSERT INTO {_options.StateTable} (actor_key, state_version, payload, updated_at) VALUES (@key, 1, @payload, @at)";
            insert.AddParameter("@key", key);
            insert.AddParameter("@payload", payload);
            insert.AddParameter("@at", now);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return 1;
        }
        catch (DbException ex) when (_dialect.IsUniqueViolation(ex))
        {
            // Another writer inserted between the failed update and this insert. That is exactly
            // the split-activation case this store exists to detect.
            throw new StateConcurrencyException(key, expectedVersion, await ReadVersionAsync(connection, key, cancellationToken).ConfigureAwait(false));
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_options.StateTable} WHERE actor_key = @key";
        command.AddParameter("@key", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the new version, or null when no row matched.</summary>
    private async Task<long?> TryUpdateAsync(
        DbConnection connection, string key, string payload, long expectedVersion, long now, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        if (expectedVersion == IStateStore.AnyVersion)
        {
            command.CommandText =
                $"UPDATE {_options.StateTable} SET state_version = state_version + 1, payload = @payload, updated_at = @at " +
                "WHERE actor_key = @key";
        }
        else
        {
            // The predicate is the concurrency check. If another writer moved the version, this
            // matches nothing, and no read-then-write window exists for them to slip through.
            command.CommandText =
                $"UPDATE {_options.StateTable} SET state_version = state_version + 1, payload = @payload, updated_at = @at " +
                "WHERE actor_key = @key AND state_version = @expected";
            command.AddParameter("@expected", expectedVersion);
        }

        command.AddParameter("@key", key);
        command.AddParameter("@payload", payload);
        command.AddParameter("@at", now);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected == 0) return null;

        return await ReadVersionAsync(connection, key, cancellationToken).ConfigureAwait(false);
    }

    private async Task<long> ReadVersionAsync(DbConnection connection, string key, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT state_version FROM {_options.StateTable} WHERE actor_key = @key";
        command.AddParameter("@key", key);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }

    private async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = _dialect.CreateConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        if (!_schemaReady && _options.AutoCreateSchema)
            await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        return connection;
    }

    private async Task EnsureSchemaAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await _schemaGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_schemaReady) return;
            await RelationalBootstrap.CreateSchemaAsync(connection, _dialect, _options, cancellationToken).ConfigureAwait(false);
            _schemaReady = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }
}

/// <summary>Shared plumbing for the relational stores.</summary>
internal static class RelationalBootstrap
{
    /// <summary>Runs the dialect's schema statements, which are all written to be idempotent.</summary>
    public static async Task CreateSchemaAsync(
        DbConnection connection, ISqlDialect dialect, RelationalStoreOptions options, CancellationToken cancellationToken)
    {
        foreach (var statement in dialect.SchemaStatements(options))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>Small helpers so the stores read as SQL rather than as ADO.NET ceremony.</summary>
internal static class DbCommandExtensions
{
    /// <summary>Adds a parameter. Every value the stores bind is a string or a long.</summary>
    public static void AddParameter(this DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        parameter.DbType = value switch
        {
            long => DbType.Int64,
            int => DbType.Int32,
            _ => DbType.String,
        };
        command.Parameters.Add(parameter);
    }
}
