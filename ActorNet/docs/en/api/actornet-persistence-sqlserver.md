# ActorNet.Persistence.SqlServer

Generated - edit the `///` comments in the source, not this file.

## SqlServerDialect

SQL Server, through Microsoft.Data.SqlClient.

Note the key length. SQL Server caps an index key at 900 bytes and NVARCHAR is two bytes per character, so an actor key column cannot exceed 450 characters - which is why `MaxKeyLength` defaults to 400 and refuses anything above 450. Get that wrong and the failure arrives as a schema error at first use rather than at configuration time.

### method `CreateConnection(String)`

### property `Instance`

A shared instance; the dialect holds no state.

### method `IsUniqueViolation(DbException)`

2627 is a primary-key or unique-constraint violation and 2601 a unique-index violation. SQL Server uses one for a constraint and the other for a bare index, and a store that checked only one would silently miss half its concurrency conflicts.

### property `Name`

### method `SchemaStatements(RelationalStoreOptions)`

SQL Server has no `CREATE TABLE IF NOT EXISTS`, so each statement is guarded by an `OBJECT_ID` check instead. The effect is the same: running the schema twice is harmless.

## SqlServerPersistence

Builds SQL Server-backed stores.

### method `EventJournal(String, MessageTypeRegistry, RelationalStoreOptions)`

An event journal in SQL Server.

### method `SnapshotStore(String, RelationalStoreOptions)`

Snapshot storage in SQL Server.

### method `StateStore(String, RelationalStoreOptions)`

State storage in SQL Server.

### method `UseSqlServer(ActorSystemOptions, String, MessageTypeRegistry, RelationalStoreOptions)`

Points all three stores at one SQL Server database.

---

[Back to the index](README.md)
