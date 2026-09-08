# ActorNet.Persistence.Sqlite

Generated - edit the `///` comments in the source, not this file.

## SqliteDialect

SQLite, through Microsoft.Data.Sqlite.

The single-node provider that survives a restart. A file on disk is not shared between cluster members, so an actor that rebalances onto another node will not find its state here - use PostgreSQL, SQL Server or MySQL for that. What SQLite is genuinely good for is a single node in production, and every test in the provider suite, because it is a real database with real SQL and real concurrency that needs no server.

### method `CreateConnection(String)`

### property `Instance`

A shared instance; the dialect holds no state.

### method `IsUniqueViolation(DbException)`

SQLite reports both a primary-key clash (1555) and a unique-index clash (2067) as extended result codes under the general constraint error 19. Matching on the extended codes keeps a NOT NULL or CHECK violation - also 19 - from being mistaken for a concurrency conflict.

### property `Name`

### method `SchemaStatements(RelationalStoreOptions)`

## SqlitePersistence

Builds SQLite-backed stores.

### method `EventJournal(String, MessageTypeRegistry, RelationalStoreOptions)`

An event journal in a SQLite database.

### method `SnapshotStore(String, RelationalStoreOptions)`

Snapshot storage in a SQLite database.

### method `StateStore(String, RelationalStoreOptions)`

State storage in a SQLite database.

### method `UseSqlite(ActorSystemOptions, String, MessageTypeRegistry, RelationalStoreOptions)`

Points all three stores at one SQLite database.

---

[Back to the index](README.md)
