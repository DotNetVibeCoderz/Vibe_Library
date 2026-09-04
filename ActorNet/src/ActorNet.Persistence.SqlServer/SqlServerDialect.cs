// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Data.Common;
using ActorNet.Persistence.Relational;
using ActorNet.Serialization;
using Microsoft.Data.SqlClient;

namespace ActorNet.Persistence.SqlServer;

/// <summary>
/// SQL Server, through Microsoft.Data.SqlClient.
/// </summary>
/// <remarks>
/// Note the key length. SQL Server caps an index key at 900 bytes and NVARCHAR is two bytes per
/// character, so an actor key column cannot exceed 450 characters - which is why
/// <see cref="RelationalStoreOptions.MaxKeyLength"/> defaults to 400 and refuses anything above
/// 450. Get that wrong and the failure arrives as a schema error at first use rather than at
/// configuration time.
/// </remarks>
public sealed class SqlServerDialect : ISqlDialect
{
    /// <summary>A shared instance; the dialect holds no state.</summary>
    public static SqlServerDialect Instance { get; } = new();

    /// <inheritdoc />
    public string Name => "SQL Server";

    /// <inheritdoc />
    public DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

    /// <inheritdoc />
    /// <remarks>
    /// SQL Server has no <c>CREATE TABLE IF NOT EXISTS</c>, so each statement is guarded by an
    /// <c>OBJECT_ID</c> check instead. The effect is the same: running the schema twice is
    /// harmless.
    /// </remarks>
    public IReadOnlyList<string> SchemaStatements(RelationalStoreOptions options) =>
    [
        $"""
         IF OBJECT_ID(N'{options.StateTable}', N'U') IS NULL
         CREATE TABLE {options.StateTable} (
             actor_key     NVARCHAR({options.MaxKeyLength}) NOT NULL PRIMARY KEY,
             state_version BIGINT                            NOT NULL,
             payload       NVARCHAR(MAX)                     NOT NULL,
             updated_at    BIGINT                            NOT NULL
         )
         """,

        $"""
         IF OBJECT_ID(N'{options.EventTable}', N'U') IS NULL
         CREATE TABLE {options.EventTable} (
             persistence_id NVARCHAR({options.MaxKeyLength}) NOT NULL,
             seq_no         BIGINT                            NOT NULL,
             type_alias     NVARCHAR(450)                     NOT NULL,
             payload        NVARCHAR(MAX)                     NOT NULL,
             created_at     BIGINT                            NOT NULL,
             CONSTRAINT PK_{options.EventTable} PRIMARY KEY (persistence_id, seq_no)
         )
         """,

        $"""
         IF OBJECT_ID(N'{options.SnapshotTable}', N'U') IS NULL
         CREATE TABLE {options.SnapshotTable} (
             persistence_id NVARCHAR({options.MaxKeyLength}) NOT NULL PRIMARY KEY,
             seq_no         BIGINT                            NOT NULL,
             payload        NVARCHAR(MAX)                     NOT NULL,
             created_at     BIGINT                            NOT NULL
         )
         """,
    ];

    /// <inheritdoc />
    /// <remarks>
    /// 2627 is a primary-key or unique-constraint violation and 2601 a unique-index violation.
    /// SQL Server uses one for a constraint and the other for a bare index, and a store that
    /// checked only one would silently miss half its concurrency conflicts.
    /// </remarks>
    public bool IsUniqueViolation(DbException exception) =>
        exception is SqlException sql && sql.Errors.Cast<SqlError>().Any(e => e.Number is 2627 or 2601);
}

/// <summary>Builds SQL Server-backed stores.</summary>
public static class SqlServerPersistence
{
    /// <summary>State storage in SQL Server.</summary>
    public static IStateStore StateStore(string connectionString, RelationalStoreOptions? options = null) =>
        new RelationalStateStore(SqlServerDialect.Instance, connectionString, options);

    /// <summary>An event journal in SQL Server.</summary>
    public static IEventJournal EventJournal(string connectionString, MessageTypeRegistry? types = null, RelationalStoreOptions? options = null) =>
        new RelationalEventJournal(SqlServerDialect.Instance, connectionString, types, options);

    /// <summary>Snapshot storage in SQL Server.</summary>
    public static ISnapshotStore SnapshotStore(string connectionString, RelationalStoreOptions? options = null) =>
        new RelationalSnapshotStore(SqlServerDialect.Instance, connectionString, options);

    /// <summary>Points all three stores at one SQL Server database.</summary>
    public static ActorSystemOptions UseSqlServer(
        this ActorSystemOptions options, string connectionString, MessageTypeRegistry? types = null, RelationalStoreOptions? storeOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.StateStore = StateStore(connectionString, storeOptions);
        options.EventJournal = EventJournal(connectionString, types, storeOptions);
        options.SnapshotStore = SnapshotStore(connectionString, storeOptions);
        return options;
    }
}
