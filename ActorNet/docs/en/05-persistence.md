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

Three seams, swapped through options:

```csharp
options.StateStore    = new FileStateStore("./data/state");
options.EventJournal  = new FileEventJournal("./data/journal", types);
options.SnapshotStore = new FileSnapshotStore("./data/snapshots");
```

| Store | Survives deactivation | Survives a restart | Shared between nodes |
| --- | --- | --- | --- |
| `InMemory*` (default) | yes | no | no |
| `File*` | yes | yes | no |
| A database provider | yes | yes | yes — **not built yet** |

The in-memory stores are the default because they make the framework work out of the box and tests
fast. They are the wrong choice the moment the state matters. The file stores make "stop it, start
it again, the balance is still there" demonstrable rather than merely claimed — write to a
temporary file and move it into place, because a half-written JSON file is unrecoverable.

**Neither is shared between nodes.** In a real cluster, an actor that rebalances onto another node
must find its state there, which means a store both nodes can read. A PostgreSQL provider is the
top item on the [roadmap](../../Plan.md).

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
