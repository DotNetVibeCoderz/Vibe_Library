// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Data.Common;
using ActorNet.Persistence.Relational;
using ActorNet.Serialization;
using MySqlConnector;

namespace ActorNet.Persistence.MySql;

/// <summary>
/// MySQL and MariaDB, through MySqlConnector.
/// </summary>
/// <remarks>
/// MySqlConnector rather than the Oracle-published MySql.Data: it is the async-native driver, and
/// every statement here is issued asynchronously. A driver that blocks a thread per call would
/// undo the point of an actor runtime that never blocks one.
/// </remarks>
public sealed class MySqlDialect : ISqlDialect
{
    /// <summary>A shared instance; the dialect holds no state.</summary>
    public static MySqlDialect Instance { get; } = new();

    /// <inheritdoc />
    public string Name => "MySQL";

    /// <inheritdoc />
    public DbConnection CreateConnection(string connectionString) => new MySqlConnection(connectionString);

    /// <inheritdoc />
    /// <remarks>
    /// The character set is pinned to utf8mb4 rather than left to the server default, because on
    /// an older server that default is <c>utf8</c> - which is three bytes and cannot store an emoji
    /// or anything else outside the basic plane. An actor key or a JSON payload that silently
    /// fails to round-trip is a worse outcome than a slightly larger index.
    /// </remarks>
    public IReadOnlyList<string> SchemaStatements(RelationalStoreOptions options) =>
    [
        $"""
         CREATE TABLE IF NOT EXISTS {options.StateTable} (
             actor_key     VARCHAR({options.MaxKeyLength}) NOT NULL,
             state_version BIGINT                          NOT NULL,
             payload       LONGTEXT                        NOT NULL,
             updated_at    BIGINT                          NOT NULL,
             PRIMARY KEY (actor_key)
         ) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin
         """,

        $"""
         CREATE TABLE IF NOT EXISTS {options.EventTable} (
             persistence_id VARCHAR({options.MaxKeyLength}) NOT NULL,
             seq_no         BIGINT                          NOT NULL,
             type_alias     VARCHAR(512)                    NOT NULL,
             payload        LONGTEXT                        NOT NULL,
             created_at     BIGINT                          NOT NULL,
             PRIMARY KEY (persistence_id, seq_no)
         ) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin
         """,

        $"""
         CREATE TABLE IF NOT EXISTS {options.SnapshotTable} (
             persistence_id VARCHAR({options.MaxKeyLength}) NOT NULL,
             seq_no         BIGINT                          NOT NULL,
             payload        LONGTEXT                        NOT NULL,
             created_at     BIGINT                          NOT NULL,
             PRIMARY KEY (persistence_id)
         ) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin
         """,
    ];

    /// <inheritdoc />
    /// <remarks>1062 is <c>ER_DUP_ENTRY</c>, which covers both a primary key and a unique index.</remarks>
    public bool IsUniqueViolation(DbException exception) =>
        exception is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry };
}

/// <summary>Builds MySQL-backed stores.</summary>
public static class MySqlPersistence
{
    /// <summary>State storage in MySQL.</summary>
    public static IStateStore StateStore(string connectionString, RelationalStoreOptions? options = null) =>
        new RelationalStateStore(MySqlDialect.Instance, connectionString, options);

    /// <summary>An event journal in MySQL.</summary>
    public static IEventJournal EventJournal(string connectionString, MessageTypeRegistry? types = null, RelationalStoreOptions? options = null) =>
        new RelationalEventJournal(MySqlDialect.Instance, connectionString, types, options);

    /// <summary>Snapshot storage in MySQL.</summary>
    public static ISnapshotStore SnapshotStore(string connectionString, RelationalStoreOptions? options = null) =>
        new RelationalSnapshotStore(MySqlDialect.Instance, connectionString, options);

    /// <summary>Points all three stores at one MySQL database.</summary>
    public static ActorSystemOptions UseMySql(
        this ActorSystemOptions options, string connectionString, MessageTypeRegistry? types = null, RelationalStoreOptions? storeOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.StateStore = StateStore(connectionString, storeOptions);
        options.EventJournal = EventJournal(connectionString, types, storeOptions);
        options.SnapshotStore = SnapshotStore(connectionString, storeOptions);
        return options;
    }
}
