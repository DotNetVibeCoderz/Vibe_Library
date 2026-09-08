# ActorNet.Persistence.PostgreSql

Generated - edit the `///` comments in the source, not this file.

## PostgreSqlDialect

PostgreSQL, through Npgsql.

The default recommendation for a cluster. Persistence is what makes rebalancing work - an actor whose key moves is flushed on one node and reloaded on another - and that needs a store every member can read.

### method `CreateConnection(String)`

### property `Instance`

A shared instance; the dialect holds no state.

### method `IsUniqueViolation(DbException)`

SQLSTATE 23505 is `unique_violation`, and PostgreSQL is exact about it.

### property `Name`

### method `SchemaStatements(RelationalStoreOptions)`

Payloads are `text` rather than `jsonb`. jsonb would allow querying inside a stored event, which is tempting and wrong here: it re-orders object keys and normalises numbers, so a round trip is no longer byte-identical, and this store's contract is that what comes back is what went in.

## PostgreSqlPersistence

Builds PostgreSQL-backed stores.

### method `EventJournal(String, MessageTypeRegistry, RelationalStoreOptions)`

An event journal in PostgreSQL.

### method `SnapshotStore(String, RelationalStoreOptions)`

Snapshot storage in PostgreSQL.

### method `StateStore(String, RelationalStoreOptions)`

State storage in PostgreSQL.

### method `UsePostgreSql(ActorSystemOptions, String, MessageTypeRegistry, RelationalStoreOptions)`

Points all three stores at one PostgreSQL database.

---

[Back to the index](README.md)
