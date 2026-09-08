# ActorNet.Persistence.Redis

Generated - edit the `///` comments in the source, not this file.

## RedisEventJournal

An append-only event journal in Redis, one sorted set per stream.

A sorted set scored by sequence number rather than a list, because the journal needs three things a list cannot do well: read forward from an arbitrary sequence, find the tip in constant time, and drop everything up to a snapshot. `ZRANGEBYSCORE`, `ZRANGE -1` and `ZREMRANGEBYSCORE` are each one command.

### method `AppendAsync(String, IReadOnlyList<Object>, Int64, CancellationToken)`

### method `DeleteToAsync(String, Int64, CancellationToken)`

### method `HighestSequenceAsync(String, CancellationToken)`

### method `ReadAsync(String, Int64, CancellationToken)`

## RedisPersistence

Builds Redis-backed stores.

### method `EventJournal(IConnectionMultiplexer, MessageTypeRegistry, RedisStoreOptions)`

An event journal in Redis.

### method `SnapshotStore(IConnectionMultiplexer, RedisStoreOptions)`

Snapshot storage in Redis.

### method `StateStore(IConnectionMultiplexer, RedisStoreOptions)`

State storage in Redis.

### method `UseRedis(ActorSystemOptions, IConnectionMultiplexer, MessageTypeRegistry, RedisStoreOptions)`

Points all three stores at one Redis connection.

The connection is not owned by the stores and is not disposed with the actor system: `IConnectionMultiplexer` is designed to be shared for the life of the application, and an application usually has other things using it.

## RedisSnapshotStore

Snapshot storage in Redis, one key per persistence id.

Last write wins, with no version check - a snapshot is an optimization over replay, so losing a race here costs a slower recovery rather than a wrong answer.

### method `DeleteAsync(String, CancellationToken)`

### method `LoadAsync<T0>(String, CancellationToken)`

### method `SaveAsync<T0>(String, T0, Int64, CancellationToken)`

## RedisStateStore

State storage in Redis, one hash per actor.

The version check and the write have to be one indivisible step, or two activations of the same actor can both read version 4 and both write version 5. Redis has no compare-and-set across fields, so the write is a Lua script: Redis runs a script to completion without interleaving another command, which is exactly the guarantee needed and is cheaper than `WATCH`/ `MULTI` with a retry loop.

A caveat worth stating plainly: Redis persistence is configurable and often off. With the default RDB snapshotting, a crash loses the last few seconds of writes. That is fine for a cache and not fine for a ledger - use a relational provider when losing a write is unacceptable.

### method `DeleteAsync(String, CancellationToken)`

### method `ReadAsync<T0>(String, CancellationToken)`

### method `WriteAsync<T0>(String, T0, Int64, CancellationToken)`

## RedisStoreOptions

Key naming and behaviour shared by the Redis stores.

### property `Database`

Which logical database to use.

### property `KeyPrefix`

Prefix for every key this library writes, so it can share a Redis instance with other applications - and so a stray `KEYS actornet:*` shows exactly what belongs to it.

---

[Back to the index](README.md)
