# Persistence

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[Bahasa Indonesia](../id/05-persistensi.md) · [Docs index](README.md)*

## Two models

| | `PersistentActor<TState>` | `EventSourcedActor<TState>` |
| --- | --- | --- |
| Stores | The current state | Everything that happened |
| Write | On deactivation, or when you ask | Once per event |
| Recovery | One read | Snapshot, then replay the rest |
| Answers | "What is the balance?" | "Why is the balance this?" |
| Cost | One row per actor | A growing stream per actor |

Pick state persistence when the current value is the truth. Pick event sourcing when the history
is — a ledger, an audit trail, anything where "how did we get here" is a real question.

## State persistence

```csharp
public sealed class DeviceState
{
    public double Latest { get; set; }
    public long Readings { get; set; }
}

public sealed class DeviceActor : PersistentActor<DeviceState>
{
    protected override int SaveEvery => 100;   // checkpoint, on top of the flush on the way out

    protected override async Task ReceiveAsync(object message, CancellationToken ct)
    {
        if (message is SensorReading reading)
        {
            State.Latest = reading.Celsius;
            State.Readings++;
        }
    }
}
```

`State` is loaded before the first message and written back on deactivation. `IsNew` tells you
whether anything was there.

**Write-on-deactivate is the default because it turns N updates into one store write.** It also
means a hard process kill loses everything since the last flush. Three ways to narrow that:

```csharp
protected override int SaveEvery => 100;         // every 100 messages
await SaveStateAsync(ct);                        // right now
protected override string PersistenceKey => ...; // a custom key, if the address is not right
```

A supervision stop skips the flush entirely — see [Supervision](04-supervision.md).

## Event sourcing

```csharp
public sealed class BankAccountActor : EventSourcedActor<AccountState>
{
    protected override long SnapshotEvery => 200;

    // The one place state changes. Runs again on every recovery, so it must be pure.
    protected override void Apply(object domainEvent)
    {
        switch (domainEvent)
        {
            case Deposited d: State.Balance += d.Amount; break;
            case Withdrawn w: State.Balance -= w.Amount; break;
        }
    }

    protected override async Task ReceiveAsync(object message, CancellationToken ct)
    {
        switch (message)
        {
            case Withdraw w when w.Amount > State.Balance:
                // Refused, so nothing is written. An overdraft that did not happen is not history.
                await Context.ReplyAsync(new Declined("Insufficient funds.", State.Balance), ct);
                break;

            case Withdraw w:
                await PersistAsync(new Withdrawn(w.Amount, DateTimeOffset.UtcNow), ct);
                await Context.ReplyAsync(new Accepted(State.Balance), ct);
                break;
        }
    }
}
```

Three rules make this work.

**Commands are validated; events are facts.** `Withdraw` can be refused. `Withdrawn` cannot — it
already happened, and `Apply` must accept it unconditionally.

**`PersistAsync` writes before it applies.** Applying first would let the actor acknowledge a change
the journal then refused, which is the one failure an event-sourced actor must not have.

**`Apply` must be pure.** It runs again on every single recovery. A charge, an email or an HTTP call
inside a fold happens again every time the actor activates. Guard side effects with `IsRecovering`,
or put them in the command handler where they belong.

Persisting *during* recovery is refused outright with an `ActorNetException`. Appending to the
stream you are replaying grows it by one event per activation, and each of those is replayed next
time.

## Snapshots

```csharp
protected override long SnapshotEvery => 200;
protected override bool TruncateOnSnapshot => false;   // default
```

Recovery loads the newest snapshot and replays only the events after it. A snapshot is an
**optimization, never a source of truth** — deleting every snapshot must leave the system correct,
just slower.

`TruncateOnSnapshot` is off by default because the audit trail is usually the point of event
sourcing, and a snapshot is not one.

## The stores

Three seams, swapped through options. Every provider passes the same conformance suite, so they are
interchangeable.

| Provider | Package | Survives a restart | Shared between nodes | Verified |
| --- | --- | --- | --- | --- |
| In-memory (default) | built in | no | no | in the suite |
| Files | built in | yes | no | in the suite |
| SQLite | `ActorNet.Persistence.Sqlite` | yes | no | in the suite, on every run |
| PostgreSQL | `ActorNet.Persistence.PostgreSql` | yes | **yes** | in CI, against a real server |
| SQL Server | `ActorNet.Persistence.SqlServer` | yes | **yes** | in CI, against a real server |
| MySQL / MariaDB | `ActorNet.Persistence.MySql` | yes | **yes** | in CI, against a real server |
| Redis | `ActorNet.Persistence.Redis` | configurable | **yes** | in CI, against a real server |

```csharp
options.UsePostgreSql("Host=db;Database=actornet;Username=app;Password=…", system.Serializer.Types);
options.UseSqlServer("Server=db;Database=actornet;…", types);
options.UseMySql("Server=db;Database=actornet;…", types);
options.UseSqlite("Data Source=./data/actornet.db", types);
options.UseRedis(ConnectionMultiplexer.Connect("localhost:6379"), types);
```

Or wire the three stores individually, which is what you want when the journal and the state belong
in different places:

```csharp
options.StateStore    = PostgreSqlPersistence.StateStore(connectionString);
options.EventJournal  = PostgreSqlPersistence.EventJournal(connectionString, types);
options.SnapshotStore = RedisPersistence.SnapshotStore(redis);
```

### Choosing one

**In-memory** is the default because it makes the framework work out of the box and tests fast. It
survives deactivation, not a process restart, and nothing is shared. Development and tests only.

**Files** make "stop it, start it again, the balance is still there" demonstrable rather than merely
claimed. One node only.

**SQLite** is the same promise with a real database underneath - real SQL, real constraints, real
concurrency. Still one node: a file is not shared. It is also what the conformance suite runs
against on every single test run, which makes it the best-exercised provider here.

**PostgreSQL, SQL Server, MySQL** are the cluster answer. Rebalancing works by deactivating an actor
on one node and reactivating it on another, and that only recovers state if both nodes can read the
same store.

**Redis** is shared and fast, with a caveat worth stating plainly: Redis persistence is configurable
and often off, and with default RDB snapshotting a crash loses the last few seconds of writes. Fine
for a projection or a cache-shaped actor; not fine for a ledger.

### The schema

Three tables, created on first use:

```
actornet_state      actor_key, state_version, payload, updated_at
actornet_events     persistence_id, seq_no, type_alias, payload, created_at   PK (persistence_id, seq_no)
actornet_snapshots  persistence_id, seq_no, payload, created_at
```

Turn auto-creation off and hand the DDL to whatever owns your schema:

```csharp
var options = new RelationalStoreOptions { AutoCreateSchema = false, TablePrefix = "app_" };
foreach (var statement in RelationalSchema.StatementsFor(PostgreSqlDialect.Instance, options))
    Console.WriteLine(statement);
```

Two details that are not arbitrary:

**Actor keys are capped at 400 characters.** The column is a primary key on four databases at once.
SQL Server caps an index key at 900 bytes and its `NVARCHAR` is two bytes per character, which puts
the ceiling at 450; MySQL's InnoDB caps it at 3072 bytes, which with `utf8mb4` is 768. 400 clears
both, and an actor address that long is already a design problem.

**The journal's primary key is `(persistence_id, seq_no)`, and that is what makes concurrent appends
safe** - not a lock and not an isolation level. An append reads the tip, checks it, and inserts at
tip+1; if another activation got there first, the database refuses the insert and the loser is told.

**Timestamps are Unix milliseconds in a `BIGINT`.** Date and time types are where the four databases
differ most, and none of the stores ever compares or ranges on the column - it is informational.

### Writing a provider

```csharp
public interface ISqlDialect
{
    string Name { get; }
    DbConnection CreateConnection(string connectionString);
    IReadOnlyList<string> SchemaStatements(RelationalStoreOptions options);
    bool IsUniqueViolation(DbException exception);
}
```

Four members, because every statement the stores issue is plain SQL all four databases accept
unchanged. A dialect that had to rewrite the DML would be a sign the DML had drifted somewhere
non-portable.

`IsUniqueViolation` is load-bearing rather than cosmetic: both stores rely on the primary key to
make a concurrent write fail rather than interleave, so misreporting a key clash as an ordinary
error turns a detectable conflict into a lost write.

For a non-relational store, implement `IStateStore`, `IEventJournal` and `ISnapshotStore` directly -
the Redis provider is the worked example - and run
`PersistenceProviderConformance` against it.

### A subtlety in the in-memory stores

They deep-copy on the way in and out. Without that, the store holds a reference to the object the
actor keeps mutating — so a snapshot taken at sequence 20 reads back as the state at sequence 25,
and recovery double-applies everything between. That bug existed, a test caught it, and the copy is
what a database provider gets for free by serializing.

## Concurrency control

Every write carries the version it was read at:

```csharp
_version = await _store.WriteAsync(key, State, _version, ct);
```

A mismatch throws `StateConcurrencyException`. The runtime keeps one activation per key per
cluster, so this should not happen in normal operation — it shows up during the brief overlap while
an actor hands off between nodes. Surfacing it beats letting the loser silently overwrite the
winner.

## Writing a provider

```csharp
public interface IStateStore
{
    Task<StoredState<T>?> ReadAsync<T>(string key, CancellationToken ct = default);
    Task<long> WriteAsync<T>(string key, T state, long expectedVersion = -1, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}
```

Three methods. `IEventJournal` adds append, read-forward, highest-sequence and truncate; expected
sequence is what stops two activations interleaving events into one stream.

Two things a provider must get right: **serialize on write** (see the subtlety above), and **honour
the expected version** — a store that ignores it cannot detect a split-brain write.

## Next

- [Clustering](06-clustering.md) — why a shared store matters
- [Supervision](04-supervision.md) — why a failed actor does not flush
