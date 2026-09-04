// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Data.Common;

namespace ActorNet.Persistence.Relational;

/// <summary>
/// The per-database differences, and only those.
/// </summary>
/// <remarks>
/// <para>
/// Every statement the stores issue is plain SQL that all four supported databases accept
/// unchanged - selects, an update with a version predicate, inserts, a <c>MAX()</c>, a ranged
/// delete. What actually differs between them is smaller than it looks: how to open a connection,
/// what a text column is called, and how a unique-key violation is reported.
/// </para>
/// <para>
/// That is why this seam is four members rather than a query builder. A dialect that had to
/// rewrite the DML would be a sign the DML had drifted somewhere non-portable.
/// </para>
/// </remarks>
public interface ISqlDialect
{
    /// <summary>Name of the database, for diagnostics and error messages.</summary>
    string Name { get; }

    /// <summary>Creates an unopened connection.</summary>
    DbConnection CreateConnection(string connectionString);

    /// <summary>
    /// The statements that create the three tables, each guarded so running them twice is
    /// harmless.
    /// </summary>
    IReadOnlyList<string> SchemaStatements(RelationalStoreOptions options);

    /// <summary>
    /// True when this exception is a primary-key or unique-index violation.
    /// </summary>
    /// <remarks>
    /// This is load-bearing rather than cosmetic. Both stores rely on the primary key to make a
    /// concurrent write fail rather than interleave: an event append inserts at sequence N+1 and
    /// lets the database refuse it if another activation got there first. Misreporting that as an
    /// ordinary error would turn a detectable conflict into a lost write.
    /// </remarks>
    bool IsUniqueViolation(DbException exception);
}

/// <summary>Table naming and behaviour shared by every relational provider.</summary>
public sealed class RelationalStoreOptions
{
    /// <summary>
    /// Prefix for the three table names, so several applications can share a schema.
    /// </summary>
    public string TablePrefix { get; set; } = "actornet_";

    /// <summary>Table holding <see cref="PersistentActor{TState}"/> state.</summary>
    public string StateTable => TablePrefix + "state";

    /// <summary>Table holding the event journal.</summary>
    public string EventTable => TablePrefix + "events";

    /// <summary>Table holding snapshots.</summary>
    public string SnapshotTable => TablePrefix + "snapshots";

    /// <summary>
    /// Create the tables on first use.
    /// </summary>
    /// <remarks>
    /// Convenient in development and questionable in production, where schema changes usually
    /// belong to a migration tool that the application does not own. Turn it off and run
    /// <see cref="RelationalSchema.StatementsFor"/> through whatever manages your schema.
    /// </remarks>
    public bool AutoCreateSchema { get; set; } = true;

    /// <summary>
    /// Longest actor key the schema can store.
    /// </summary>
    /// <remarks>
    /// 400 rather than something rounder because it has to be a primary key on all four
    /// databases at once. SQL Server caps an index key at 900 bytes, and its NVARCHAR is two bytes
    /// per character, which puts the ceiling at 450; MySQL's InnoDB caps it at 3072 bytes, which
    /// with utf8mb4 is 768. 400 clears both with room to spare, and an actor address that long is
    /// already a design problem.
    /// </remarks>
    public int MaxKeyLength { get; set; } = 400;

    /// <summary>Throws when the options cannot produce a working schema.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TablePrefix))
            throw new ArgumentException("TablePrefix must not be empty.", nameof(TablePrefix));

        // The prefix reaches SQL as an identifier rather than a parameter, because table names
        // cannot be parameterised. Restricting it to identifier characters is what keeps that from
        // being an injection point.
        foreach (var c in TablePrefix)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                throw new ArgumentException(
                    $"TablePrefix may only contain letters, digits and underscores; '{c}' is not allowed. " +
                    "It is interpolated into SQL as an identifier, which cannot be parameterised.",
                    nameof(TablePrefix));
        }

        if (MaxKeyLength is < 16 or > 450)
            throw new ArgumentOutOfRangeException(nameof(MaxKeyLength), MaxKeyLength,
                "MaxKeyLength must be between 16 and 450; above 450 the key no longer fits a SQL Server index.");
    }
}

/// <summary>The schema, exposed so it can be handed to a migration tool instead of auto-created.</summary>
public static class RelationalSchema
{
    /// <summary>The statements that create the three tables for a dialect.</summary>
    public static IReadOnlyList<string> StatementsFor(ISqlDialect dialect, RelationalStoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        options ??= new RelationalStoreOptions();
        options.Validate();
        return dialect.SchemaStatements(options);
    }
}
