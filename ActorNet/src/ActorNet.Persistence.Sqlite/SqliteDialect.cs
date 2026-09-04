// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Data.Common;
using ActorNet.Persistence.Relational;
using ActorNet.Serialization;
using Microsoft.Data.Sqlite;

namespace ActorNet.Persistence.Sqlite;

/// <summary>
/// SQLite, through Microsoft.Data.Sqlite.
/// </summary>
/// <remarks>
/// The single-node provider that survives a restart. A file on disk is not shared between cluster
/// members, so an actor that rebalances onto another node will not find its state here - use
/// PostgreSQL, SQL Server or MySQL for that. What SQLite is genuinely good for is a single node in
/// production, and every test in the provider suite, because it is a real database with real SQL
/// and real concurrency that needs no server.
/// </remarks>
public sealed class SqliteDialect : ISqlDialect
{
    /// <summary>A shared instance; the dialect holds no state.</summary>
    public static SqliteDialect Instance { get; } = new();

    /// <inheritdoc />
    public string Name => "SQLite";

    /// <inheritdoc />
    public DbConnection CreateConnection(string connectionString) => new SqliteConnection(connectionString);

    /// <inheritdoc />
    public IReadOnlyList<string> SchemaStatements(RelationalStoreOptions options) =>
    [
        $"""
         CREATE TABLE IF NOT EXISTS {options.StateTable} (
             actor_key     TEXT    NOT NULL PRIMARY KEY,
             state_version INTEGER NOT NULL,
             payload       TEXT    NOT NULL,
             updated_at    INTEGER NOT NULL
         )
         """,

        $"""
         CREATE TABLE IF NOT EXISTS {options.EventTable} (
             persistence_id TEXT    NOT NULL,
             seq_no         INTEGER NOT NULL,
             type_alias     TEXT    NOT NULL,
             payload        TEXT    NOT NULL,
             created_at     INTEGER NOT NULL,
             PRIMARY KEY (persistence_id, seq_no)
         )
         """,

        $"""
         CREATE TABLE IF NOT EXISTS {options.SnapshotTable} (
             persistence_id TEXT    NOT NULL PRIMARY KEY,
             seq_no         INTEGER NOT NULL,
             payload        TEXT    NOT NULL,
             created_at     INTEGER NOT NULL
         )
         """,
    ];

    /// <inheritdoc />
    /// <remarks>
    /// SQLite reports both a primary-key clash (1555) and a unique-index clash (2067) as extended
    /// result codes under the general constraint error 19. Matching on the extended codes keeps a
    /// NOT NULL or CHECK violation - also 19 - from being mistaken for a concurrency conflict.
    /// </remarks>
    public bool IsUniqueViolation(DbException exception) =>
        exception is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 };
}

/// <summary>Builds SQLite-backed stores.</summary>
public static class SqlitePersistence
{
    /// <summary>State storage in a SQLite database.</summary>
    public static IStateStore StateStore(string connectionString, RelationalStoreOptions? options = null) =>
        new RelationalStateStore(SqliteDialect.Instance, connectionString, options);

    /// <summary>An event journal in a SQLite database.</summary>
    public static IEventJournal EventJournal(string connectionString, MessageTypeRegistry? types = null, RelationalStoreOptions? options = null) =>
        new RelationalEventJournal(SqliteDialect.Instance, connectionString, types, options);

    /// <summary>Snapshot storage in a SQLite database.</summary>
    public static ISnapshotStore SnapshotStore(string connectionString, RelationalStoreOptions? options = null) =>
        new RelationalSnapshotStore(SqliteDialect.Instance, connectionString, options);

    /// <summary>Points all three stores at one SQLite database.</summary>
    /// <example>
    /// <code>
    /// options.UseSqlite("Data Source=./data/actornet.db", system.Serializer.Types);
    /// </code>
    /// </example>
    public static ActorSystemOptions UseSqlite(
        this ActorSystemOptions options, string connectionString, MessageTypeRegistry? types = null, RelationalStoreOptions? storeOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.StateStore = StateStore(connectionString, storeOptions);
        options.EventJournal = EventJournal(connectionString, types, storeOptions);
        options.SnapshotStore = SnapshotStore(connectionString, storeOptions);
        return options;
    }
}
