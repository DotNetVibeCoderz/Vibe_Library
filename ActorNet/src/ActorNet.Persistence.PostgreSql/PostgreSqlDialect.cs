// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Data.Common;
using ActorNet.Persistence.Relational;
using ActorNet.Serialization;
using Npgsql;

namespace ActorNet.Persistence.PostgreSql;

/// <summary>
/// PostgreSQL, through Npgsql.
/// </summary>
/// <remarks>
/// The default recommendation for a cluster. Persistence is what makes rebalancing work - an actor
/// whose key moves is flushed on one node and reloaded on another - and that needs a store every
/// member can read.
/// </remarks>
public sealed class PostgreSqlDialect : ISqlDialect
{
    /// <summary>A shared instance; the dialect holds no state.</summary>
    public static PostgreSqlDialect Instance { get; } = new();

    /// <inheritdoc />
    public string Name => "PostgreSQL";

    /// <inheritdoc />
    public DbConnection CreateConnection(string connectionString) => new NpgsqlConnection(connectionString);

    /// <inheritdoc />
    /// <remarks>
    /// Payloads are <c>text</c> rather than <c>jsonb</c>. jsonb would allow querying inside a
    /// stored event, which is tempting and wrong here: it re-orders object keys and normalises
    /// numbers, so a round trip is no longer byte-identical, and this store's contract is that what
    /// comes back is what went in.
    /// </remarks>
    public IReadOnlyList<string> SchemaStatements(RelationalStoreOptions options) =>
    [
        $"""
         CREATE TABLE IF NOT EXISTS {options.StateTable} (
             actor_key     VARCHAR({options.MaxKeyLength}) NOT NULL PRIMARY KEY,
             state_version BIGINT                          NOT NULL,
             payload       TEXT                            NOT NULL,
             updated_at    BIGINT                          NOT NULL
         )
         """,

        $"""
         CREATE TABLE IF NOT EXISTS {options.EventTable} (
             persistence_id VARCHAR({options.MaxKeyLength}) NOT NULL,
             seq_no         BIGINT                          NOT NULL,
             type_alias     VARCHAR(512)                    NOT NULL,
             payload        TEXT                            NOT NULL,
             created_at     BIGINT                          NOT NULL,
             PRIMARY KEY (persistence_id, seq_no)
         )
         """,

        $"""
         CREATE TABLE IF NOT EXISTS {options.SnapshotTable} (
             persistence_id VARCHAR({options.MaxKeyLength}) NOT NULL PRIMARY KEY,
             seq_no         BIGINT                          NOT NULL,
             payload        TEXT                            NOT NULL,
             created_at     BIGINT                          NOT NULL
         )
         """,
    ];

    /// <inheritdoc />
    /// <remarks>SQLSTATE 23505 is <c>unique_violation</c>, and PostgreSQL is exact about it.</remarks>
    public bool IsUniqueViolation(DbException exception) =>
        exception is PostgresException { SqlState: "23505" };
}

/// <summary>Builds PostgreSQL-backed stores.</summary>
public static class PostgreSqlPersistence
{
    /// <summary>State storage in PostgreSQL.</summary>
    public static IStateStore StateStore(string connectionString, RelationalStoreOptions? options = null) =>
        new RelationalStateStore(PostgreSqlDialect.Instance, connectionString, options);

    /// <summary>An event journal in PostgreSQL.</summary>
    public static IEventJournal EventJournal(string connectionString, MessageTypeRegistry? types = null, RelationalStoreOptions? options = null) =>
        new RelationalEventJournal(PostgreSqlDialect.Instance, connectionString, types, options);

    /// <summary>Snapshot storage in PostgreSQL.</summary>
    public static ISnapshotStore SnapshotStore(string connectionString, RelationalStoreOptions? options = null) =>
        new RelationalSnapshotStore(PostgreSqlDialect.Instance, connectionString, options);

    /// <summary>Points all three stores at one PostgreSQL database.</summary>
    public static ActorSystemOptions UsePostgreSql(
        this ActorSystemOptions options, string connectionString, MessageTypeRegistry? types = null, RelationalStoreOptions? storeOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.StateStore = StateStore(connectionString, storeOptions);
        options.EventJournal = EventJournal(connectionString, types, storeOptions);
        options.SnapshotStore = SnapshotStore(connectionString, storeOptions);
        return options;
    }
}
