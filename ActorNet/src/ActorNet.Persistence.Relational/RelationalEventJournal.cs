// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ActorNet.Serialization;

namespace ActorNet.Persistence.Relational;

/// <summary>
/// An append-only event journal in a relational table, one row per event.
/// </summary>
/// <remarks>
/// <para>
/// The primary key is <c>(persistence_id, seq_no)</c>, and that is what makes concurrent appends
/// safe rather than a lock or a transaction isolation level. An append reads the current tip,
/// checks it against what the caller expected, and inserts at tip+1; if another activation of the
/// same actor got there first, the database refuses the insert and the loser is told, instead of
/// two writers quietly interleaving one stream.
/// </para>
/// <para>
/// Events are stored under their registered alias rather than a CLR type name, for the same reason
/// the wire protocol does: a journal read must not be able to name an arbitrary type for this
/// process to construct, and an alias is what lets a stream survive a class being renamed or moved.
/// </para>
/// </remarks>
public sealed class RelationalEventJournal : IEventJournal
{
    private readonly ISqlDialect _dialect;
    private readonly string _connectionString;
    private readonly RelationalStoreOptions _options;
    private readonly MessageTypeRegistry _types;
    private readonly JsonSerializerOptions _json = new() { IncludeFields = true };
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private volatile bool _schemaReady;

    /// <param name="types">
    /// The allow-list used to resolve event types on replay. Share the actor system's registry and
    /// an event type registered for the wire is also readable from the journal.
    /// </param>
    public RelationalEventJournal(
        ISqlDialect dialect, string connectionString, MessageTypeRegistry? types = null, RelationalStoreOptions? options = null)
    {
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _types = types ?? new MessageTypeRegistry();
        _options = options ?? new RelationalStoreOptions();
        _options.Validate();
    }

    /// <inheritdoc />
    public async Task<long> AppendAsync(
        string persistenceId, IReadOnlyList<object> events, long expectedSequence = IStateStore.AnyVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);
        ArgumentNullException.ThrowIfNull(events);

        if (persistenceId.Length > _options.MaxKeyLength)
            throw new ArgumentException(
                $"Persistence id '{persistenceId}' is {persistenceId.Length} characters; the schema stores at most {_options.MaxKeyLength}.",
                nameof(persistenceId));

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        var current = await ReadHighestAsync(connection, persistenceId, cancellationToken).ConfigureAwait(false);
        if (events.Count == 0) return current;

        if (expectedSequence != IStateStore.AnyVersion && expectedSequence != current)
            throw new StateConcurrencyException(persistenceId, expectedSequence, current);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // One transaction for the batch, so a command that produces several events either lands
        // whole or not at all. A crash midway through would otherwise leave an actor recovering
        // into a state its own handler never produced.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var domainEvent in events)
            {
                var alias = _types.AliasOf(domainEvent.GetType());
                var payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), _json);

                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    $"INSERT INTO {_options.EventTable} (persistence_id, seq_no, type_alias, payload, created_at) " +
                    "VALUES (@id, @seq, @alias, @payload, @at)";
                insert.AddParameter("@id", persistenceId);
                insert.AddParameter("@seq", ++current);
                insert.AddParameter("@alias", alias);
                insert.AddParameter("@payload", payload);
                insert.AddParameter("@at", now);

                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return current;
        }
        catch (DbException ex) when (_dialect.IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

            // Someone else appended to this stream between the tip read and the insert. The
            // primary key caught it, which is the point of putting the sequence in the key.
            var actual = await ReadHighestAsync(connection, persistenceId, CancellationToken.None).ConfigureAwait(false);
            throw new StateConcurrencyException(persistenceId, expectedSequence, actual);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<JournalEntry> ReadAsync(
        string persistenceId, long fromSequence = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT seq_no, type_alias, payload, created_at FROM {_options.EventTable} " +
            "WHERE persistence_id = @id AND seq_no > @from ORDER BY seq_no";
        command.AddParameter("@id", persistenceId);
        command.AddParameter("@from", fromSequence);

        // Streamed rather than buffered: recovery replays a whole stream, and an actor with a long
        // history should not need its entire journal resident to fold over it.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var sequence = reader.GetInt64(0);
            var alias = reader.GetString(1);
            var payload = reader.GetString(2);
            var at = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3));

            var type = _types.Resolve(alias);
            var domainEvent = JsonSerializer.Deserialize(payload, type, _json)
                              ?? throw new ActorNetException($"Event {sequence} in stream '{persistenceId}' deserialized to null.");

            yield return new JournalEntry(persistenceId, sequence, domainEvent, at);
        }
    }

    /// <inheritdoc />
    public async Task<long> HighestSequenceAsync(string persistenceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadHighestAsync(connection, persistenceId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteToAsync(string persistenceId, long toSequence, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(persistenceId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_options.EventTable} WHERE persistence_id = @id AND seq_no <= @to";
        command.AddParameter("@id", persistenceId);
        command.AddParameter("@to", toSequence);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<long> ReadHighestAsync(DbConnection connection, string persistenceId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT MAX(seq_no) FROM {_options.EventTable} WHERE persistence_id = @id";
        command.AddParameter("@id", persistenceId);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
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
