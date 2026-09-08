# ActorNet.Persistence.Relational

Generated - edit the `///` comments in the source, not this file.

## DbCommandExtensions

Small helpers so the stores read as SQL rather than as ADO.NET ceremony.

### method `AddParameter(DbCommand, String, Object)`

Adds a parameter. Every value the stores bind is a string or a long.

## ISqlDialect

The per-database differences, and only those.

Every statement the stores issue is plain SQL that all four supported databases accept unchanged - selects, an update with a version predicate, inserts, a `MAX()`, a ranged delete. What actually differs between them is smaller than it looks: how to open a connection, what a text column is called, and how a unique-key violation is reported.

That is why this seam is four members rather than a query builder. A dialect that had to rewrite the DML would be a sign the DML had drifted somewhere non-portable.

### method `CreateConnection(String)`

Creates an unopened connection.

### method `IsUniqueViolation(DbException)`

True when this exception is a primary-key or unique-index violation.

This is load-bearing rather than cosmetic. Both stores rely on the primary key to make a concurrent write fail rather than interleave: an event append inserts at sequence N+1 and lets the database refuse it if another activation got there first. Misreporting that as an ordinary error would turn a detectable conflict into a lost write.

### property `Name`

Name of the database, for diagnostics and error messages.

### method `SchemaStatements(RelationalStoreOptions)`

The statements that create the three tables, each guarded so running them twice is harmless.

## RelationalBootstrap

Shared plumbing for the relational stores.

### method `CreateSchemaAsync(DbConnection, ISqlDialect, RelationalStoreOptions, CancellationToken)`

Runs the dialect's schema statements, which are all written to be idempotent.

## RelationalEventJournal

An append-only event journal in a relational table, one row per event.

The primary key is `(persistence_id, seq_no)`, and that is what makes concurrent appends safe rather than a lock or a transaction isolation level. An append reads the current tip, checks it against what the caller expected, and inserts at tip+1; if another activation of the same actor got there first, the database refuses the insert and the loser is told, instead of two writers quietly interleaving one stream.

Events are stored under their registered alias rather than a CLR type name, for the same reason the wire protocol does: a journal read must not be able to name an arbitrary type for this process to construct, and an alias is what lets a stream survive a class being renamed or moved.

### method `RelationalEventJournal(ISqlDialect, String, MessageTypeRegistry, RelationalStoreOptions)`

- `types` — The allow-list used to resolve event types on replay. Share the actor system's registry and an event type registered for the wire is also readable from the journal.

### method `AppendAsync(String, IReadOnlyList<Object>, Int64, CancellationToken)`

### method `DeleteToAsync(String, Int64, CancellationToken)`

### method `HighestSequenceAsync(String, CancellationToken)`

### method `ReadAsync(String, Int64, CancellationToken)`

## RelationalSchema

The schema, exposed so it can be handed to a migration tool instead of auto-created.

### method `StatementsFor(ISqlDialect, RelationalStoreOptions)`

The statements that create the three tables for a dialect.

## RelationalSnapshotStore

Snapshot storage in a relational table, one row per persistence id.

A snapshot is an optimization over replay and never a source of truth, so this store is deliberately the simplest of the three: last write wins, no version check. Losing a race here costs a slower recovery, not a wrong answer - the journal still decides what the state is.

### method `DeleteAsync(String, CancellationToken)`

### method `LoadAsync<T0>(String, CancellationToken)`

### method `SaveAsync<T0>(String, T0, Int64, CancellationToken)`

## RelationalStateStore

State storage in a relational table, one row per actor.

Unlike the in-memory and file stores, this one is shared between nodes - which is the whole point. An actor whose key moves to another node during a rebalance is flushed here and reloaded there, and until a store both nodes can read exists, that handoff loses the state.

Every write is a single statement whose `WHERE` clause carries the expected version, so the version check and the write cannot be separated by another writer. No transaction is needed for that, and none is taken.

### method `DeleteAsync(String, CancellationToken)`

### method `ReadAsync<T0>(String, CancellationToken)`

### method `TryUpdateAsync(DbConnection, String, String, Int64, Int64, CancellationToken)`

Returns the new version, or null when no row matched.

### method `WriteAsync<T0>(String, T0, Int64, CancellationToken)`

## RelationalStoreOptions

Table naming and behaviour shared by every relational provider.

### property `AutoCreateSchema`

Create the tables on first use.

Convenient in development and questionable in production, where schema changes usually belong to a migration tool that the application does not own. Turn it off and run `StatementsFor` through whatever manages your schema.

### property `EventTable`

Table holding the event journal.

### property `MaxKeyLength`

Longest actor key the schema can store.

400 rather than something rounder because it has to be a primary key on all four databases at once. SQL Server caps an index key at 900 bytes, and its NVARCHAR is two bytes per character, which puts the ceiling at 450; MySQL's InnoDB caps it at 3072 bytes, which with utf8mb4 is 768. 400 clears both with room to spare, and an actor address that long is already a design problem.

### property `SnapshotTable`

Table holding snapshots.

### property `StateTable`

Table holding `PersistentActor` state.

### property `TablePrefix`

Prefix for the three table names, so several applications can share a schema.

### method `Validate`

Throws when the options cannot produce a working schema.

---

[Back to the index](README.md)
