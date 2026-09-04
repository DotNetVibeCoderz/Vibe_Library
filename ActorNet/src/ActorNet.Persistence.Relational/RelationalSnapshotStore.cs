// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Data.Common;
using System.Text.Json;

namespace ActorNet.Persistence.Relational;

/// <summary>
/// Snapshot storage in a relational table, one row per persistence id.
/// </summary>
/// <remarks>
/// A snapshot is an optimization over replay and never a source of truth, so this store is
/// deliberately the simplest of the three: last write wins, no version check. Losing a race here
/// costs a slower recovery, not a wrong answer - the journal still decides what the state is.
/// </remarks>
public sealed class RelationalSnapshotStore : ISnapshotStore
{
    private readonly ISqlDialect _dialect;
    private readonly string _connectionString;
    private readonly RelationalStoreOptions _options;
    private readonly JsonSerializerOptions _json = new() { IncludeFields = true };
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private volatile bool _schemaReady;

    public RelationalSnapshotStore(ISqlDialect dialect, string connectionString, RelationalStoreOptions? options = null)
    {
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _options = options ?? new RelationalStoreOptions();
        _options.Validate();
    }

    /// <inheritdoc />
    public async Task<StateSnapshot<T>?> LoadAsync<T>(string persistenceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT seq_no, payload, created_at FROM {_options.SnapshotTable} WHERE persistence_id = @id";
        command.AddParameter("@id", persistenceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        var sequence = reader.GetInt64(0);
        var state = JsonSerializer.Deserialize<T>(reader.GetString(1), _json)
                    ?? throw new ActorNetException($"Snapshot for '{persistenceId}' deserialized to null.");
        var at = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2));

        return new StateSnapshot<T>(state, sequence, at);
    }

    /// <inheritdoc />
    public async Task SaveAsync<T>(string persistenceId, T state, long sequence, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);
        ArgumentNullException.ThrowIfNull(state);

        if (persistenceId.Length > _options.MaxKeyLength)
            throw new ArgumentException(
                $"Persistence id '{persistenceId}' is {persistenceId.Length} characters; the schema stores at most {_options.MaxKeyLength}.",
                nameof(persistenceId));

        var payload = JsonSerializer.Serialize(state, _json);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        // Update, then insert if nothing matched - the same shape as the state store, and for the
        // same reason: it avoids a portable-upsert problem the four databases each solve
        // differently.
        await using (var update = connection.CreateCommand())
        {
            update.CommandText =
                $"UPDATE {_options.SnapshotTable} SET seq_no = @seq, payload = @payload, created_at = @at WHERE persistence_id = @id";
            update.AddParameter("@id", persistenceId);
            update.AddParameter("@seq", sequence);
            update.AddParameter("@payload", payload);
            update.AddParameter("@at", now);

            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0) return;
        }

        try
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText =
                $"INSERT INTO {_options.SnapshotTable} (persistence_id, seq_no, payload, created_at) VALUES (@id, @seq, @payload, @at)";
            insert.AddParameter("@id", persistenceId);
            insert.AddParameter("@seq", sequence);
            insert.AddParameter("@payload", payload);
            insert.AddParameter("@at", now);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbException ex) when (_dialect.IsUniqueViolation(ex))
        {
            // Another writer inserted first. Last write wins here, so retry as an update rather
            // than surfacing a conflict the caller cannot act on.
            await using var retry = connection.CreateCommand();
            retry.CommandText =
                $"UPDATE {_options.SnapshotTable} SET seq_no = @seq, payload = @payload, created_at = @at WHERE persistence_id = @id";
            retry.AddParameter("@id", persistenceId);
            retry.AddParameter("@seq", sequence);
            retry.AddParameter("@payload", payload);
            retry.AddParameter("@at", now);
            await retry.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string persistenceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_options.SnapshotTable} WHERE persistence_id = @id";
        command.AddParameter("@id", persistenceId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = _dialect.CreateConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        if (!_schemaReady && _options.AutoCreateSchema)
        {
            await _schemaGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_schemaReady)
                {
                    await RelationalBootstrap.CreateSchemaAsync(connection, _dialect, _options, cancellationToken).ConfigureAwait(false);
                    _schemaReady = true;
                }
            }
            finally
            {
                _schemaGate.Release();
            }
        }

        return connection;
    }
}
