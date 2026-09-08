# ActorNet.Persistence.MySql

Generated - edit the `///` comments in the source, not this file.

## MySqlDialect

MySQL and MariaDB, through MySqlConnector.

MySqlConnector rather than the Oracle-published MySql.Data: it is the async-native driver, and every statement here is issued asynchronously. A driver that blocks a thread per call would undo the point of an actor runtime that never blocks one.

### method `CreateConnection(String)`

### property `Instance`

A shared instance; the dialect holds no state.

### method `IsUniqueViolation(DbException)`

1062 is `ER_DUP_ENTRY`, which covers both a primary key and a unique index.

### property `Name`

### method `SchemaStatements(RelationalStoreOptions)`

The character set is pinned to utf8mb4 rather than left to the server default, because on an older server that default is `utf8` - which is three bytes and cannot store an emoji or anything else outside the basic plane. An actor key or a JSON payload that silently fails to round-trip is a worse outcome than a slightly larger index.

## MySqlPersistence

Builds MySQL-backed stores.

### method `EventJournal(String, MessageTypeRegistry, RelationalStoreOptions)`

An event journal in MySQL.

### method `SnapshotStore(String, RelationalStoreOptions)`

Snapshot storage in MySQL.

### method `StateStore(String, RelationalStoreOptions)`

State storage in MySQL.

### method `UseMySql(ActorSystemOptions, String, MessageTypeRegistry, RelationalStoreOptions)`

Points all three stores at one MySQL database.

---

[Back to the index](README.md)
