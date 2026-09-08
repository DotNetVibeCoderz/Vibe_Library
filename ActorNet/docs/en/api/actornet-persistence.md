# ActorNet.Persistence

Generated - edit the `///` comments in the source, not this file.

## CompactionPolicy

How much history to keep behind a snapshot.

- `KeepEvents` — Events to keep after the snapshot's sequence, in addition to the snapshot itself. Zero deletes everything the snapshot covers.
- `KeepFor` — The youngest events to keep regardless of `KeepEvents`, or null for no such floor.

A snapshot makes the events before it unnecessary for recovery, which is not the same as making them unwanted. They are the audit trail, the input to a projection that has not caught up, and the only way to answer a question about what happened that nobody thought to ask at the time.

So compaction takes a policy rather than a sequence number. `Aggressive` is the behaviour `DeleteToAsync` already gave; the others keep a tail.

### method `CompactionPolicy(Int64, Nullable<TimeSpan>)`

How much history to keep behind a snapshot.

- `KeepEvents` — Events to keep after the snapshot's sequence, in addition to the snapshot itself. Zero deletes everything the snapshot covers.
- `KeepFor` — The youngest events to keep regardless of `KeepEvents`, or null for no such floor.

A snapshot makes the events before it unnecessary for recovery, which is not the same as making them unwanted. They are the audit trail, the input to a projection that has not caught up, and the only way to answer a question about what happened that nobody thought to ask at the time.

So compaction takes a policy rather than a sequence number. `Aggressive` is the behaviour `DeleteToAsync` already gave; the others keep a tail.

### property `Aggressive`

Keep nothing the snapshot covers. The smallest journal, and no history.

### property `KeepAWeek`

Keep a week of history behind the snapshot.

### property `KeepEvents`

Events to keep after the snapshot's sequence, in addition to the snapshot itself. Zero deletes everything the snapshot covers.

### property `KeepFor`

The youngest events to keep regardless of `KeepEvents`, or null for no such floor.

### property `KeepRecent`

Keep the last thousand events, whatever the snapshot covers.

## CompactionResult

What a compaction did, or did not do.

- `PersistenceId` — The stream.
- `DeletedTo` — The sequence deleted up to and including. Zero when nothing was deleted.
- `Reason` — Why nothing was deleted, when nothing was.

### method `CompactionResult(String, Int64, String)`

What a compaction did, or did not do.

- `PersistenceId` — The stream.
- `DeletedTo` — The sequence deleted up to and including. Zero when nothing was deleted.
- `Reason` — Why nothing was deleted, when nothing was.

### property `Compacted`

Whether any events were removed.

### property `DeletedTo`

The sequence deleted up to and including. Zero when nothing was deleted.

### property `PersistenceId`

The stream.

### property `Reason`

Why nothing was deleted, when nothing was.

## EventSourcedActor&lt;T0&gt;

A virtual actor whose state is a fold over an append-only event stream.

The Akka-style persistence model, and the CQRS half of the requirements. Instead of storing what the state *is*, the actor stores what *happened*: a handler validates a command and calls `PersistAsync`, the event is appended, and `Apply` folds it into the state. Recovery replays the same folds, so there is exactly one code path that can change state and history is never lost to an overwrite.

The ordering inside `PersistAsync` matters: the event is written *before* it is applied. Applying first would let an actor acknowledge a change that the journal then refused, which is the one failure an event-sourced actor must not have.

Snapshots are an optimization over replay, never a source of truth: recovery loads the newest snapshot and replays only the events after it. Set `SnapshotEvery` once a stream is long enough that replaying it is slower than reading a snapshot.

### method `Apply(Object)`

Folds one event into the state. Must be pure: it runs again on every recovery.

### property `IsRecovering`

True while recovery is replaying history, false once the actor is live.

Check this before any side effect. Replay re-runs every `Apply` call the actor has ever made, so a charge or an email sent from inside a fold happens again on every activation.

### method `OnActivateAsync(CancellationToken)`

Recovers by loading the newest snapshot and replaying everything after it.

### method `OnRecoveryCompletedAsync(Int32, CancellationToken)`

Runs once recovery has finished, with the number of events replayed.

### method `PersistAllAsync(IReadOnlyList<Object>, CancellationToken)`

Appends several events as one journal write, then folds them in order.

Use this rather than several `PersistAsync` calls when a command produces events that only make sense together - the single append is what stops a crash from leaving half of them in the stream.

### method `PersistAsync(Object, CancellationToken)`

Appends one event, then folds it in.

### property `PersistenceId`

The stream id. Defaults to the actor's address.

### method `ReadHistoryAsync(Int64, CancellationToken)`

Reads this actor's history, oldest first. For projections and audit views.

### method `SaveSnapshotAsync(CancellationToken)`

Writes a snapshot at the current sequence.

### property `Sequence`

Sequence number of the last event this actor has applied.

### property `SnapshotEvery`

Take a snapshot every this many events. Zero disables snapshotting.

### property `State`

The folded state. Never null after activation.

### property `TruncateOnSnapshot`

Delete journal entries covered by a snapshot once it is written. Off by default: the whole point of a journal is often the audit trail, and a snapshot is not one.

## FileEventJournal

An event journal as one append-only JSON-lines file per stream.

JSON Lines rather than a JSON array, because appending to an array means rewriting the file: with one object per line an append is a seek to the end and a write, which is what an append-only log should cost.

### method `FileEventJournal(String, MessageTypeRegistry)`

Creates a journal rooted at `directory`.

- `types` — The allow-list used to resolve event types on replay. Sharing the actor system's registry means an event type registered for the wire is also readable from disk.

### method `AppendAsync(String, IReadOnlyList<Object>, Int64, CancellationToken)`

### method `DeleteToAsync(String, Int64, CancellationToken)`

### method `HighestSequenceAsync(String, CancellationToken)`

### method `ReadAsync(String, Int64, CancellationToken)`

## FileSnapshotStore

Snapshots as one JSON file per persistence id, alongside a `FileEventJournal`.

### method `FileSnapshotStore(String)`

Snapshots as one JSON file per persistence id, alongside a `FileEventJournal`.

### method `DeleteAsync(String, CancellationToken)`

### method `LoadAsync<T0>(String, CancellationToken)`

### method `SaveAsync<T0>(String, T0, Int64, CancellationToken)`

## FileStateStore

State storage as one JSON file per key, under a directory.

The point of this store is that it survives a process restart without asking anyone to run a database - which is what the samples and the CLI demos need to show a virtual actor genuinely reloading its state. It is not a substitute for a real provider under load: every write is a file write, and there is no transaction across keys.

Writes go to a temporary file and are then moved into place. A half-written JSON file is unrecoverable state loss, and a move is the closest thing to atomic that a filesystem offers.

### method `FileStateStore(String)`

Creates a store rooted at `directory`, creating it if needed.

### method `DeleteAsync(String, CancellationToken)`

### method `PathFor(String)`

Maps a key to a filename.

Keys are user data and contain `/` by construction, so they are hashed rather than used as paths - both to keep the key's own separators from creating directories and to stop a key like `../../etc/passwd` from escaping the store's root.

### method `ReadAsync<T0>(String, CancellationToken)`

### method `WriteAsync<T0>(String, T0, Int64, CancellationToken)`

## IEventJournal

An append-only event log, one stream per persistence id.

This is the CQRS half of the persistence story: state is a fold over the stream rather than a row that gets overwritten, so history is replayable and a projection can be rebuilt from scratch.

### method `AppendAsync(String, IReadOnlyList<Object>, Int64, CancellationToken)`

Appends events and returns the sequence number of the last one.

- `expectedSequence` — The highest sequence the caller has seen, or `AnyVersion`. Mismatches throw `StateConcurrencyException`, which is what stops two activations of the same actor from interleaving events.

### method `DeleteToAsync(String, Int64, CancellationToken)`

Drops events up to and including `toSequence`. Only safe once a snapshot at or after that sequence exists.

### method `HighestSequenceAsync(String, CancellationToken)`

The highest sequence in a stream, or zero when it is empty.

### method `ReadAsync(String, Int64, CancellationToken)`

Reads a stream forward from `fromSequence`, exclusive.

## IPersistence

The three persistence seams, as an actor sees them.

### property `Journal`

Where `EventSourcedActor` appends events.

### property `Snapshots`

Where event-sourced actors keep snapshots.

### property `State`

Where `PersistentActor` keeps state.

## IProjection

A read model built by folding a journal, kept up to date by being run again.

The events are the record and this is a view of them, so it can always be thrown away and rebuilt - which is the property that makes a projection worth having, and the reason `ProjectionRunner` can be pointed at offset zero.

### method `ApplyAsync(JournalEntry, CancellationToken)`

Folds one event in.

Called at least once per event. The runner stores its position after handling rather than before, so an interruption replays rather than skips - which makes this the place that has to tolerate seeing the same event twice.

### property `Name`

The name its position is stored under. Changing it replays from the beginning.

## ISnapshotStore

Snapshot storage, so recovery does not have to replay a stream from the beginning.

### method `DeleteAsync(String, CancellationToken)`

Removes the snapshot for a persistence id.

### method `LoadAsync<T0>(String, CancellationToken)`

Loads the newest snapshot, or null when there is none.

### method `SaveAsync<T0>(String, T0, Int64, CancellationToken)`

Stores a snapshot taken at `sequence`.

## IStateStore

Key/value state storage for `PersistentActor`.

The seam a database provider plugs into. Implementations only need three operations, and versioning is optional in the sense that a store may always accept `AnyVersion` - but a store that ignores versions cannot detect a split-brain write.

### field `AnyVersion`

Version value that means "write regardless of what is there".

### method `DeleteAsync(String, CancellationToken)`

Removes a key. Succeeds whether or not it existed.

### method `ReadAsync<T0>(String, CancellationToken)`

Reads a key, or null when nothing has been written under it.

### method `WriteAsync<T0>(String, T0, Int64, CancellationToken)`

Writes a key and returns the new version.

- `expectedVersion` — The version the caller last read, or `AnyVersion`. A mismatch throws `StateConcurrencyException`.

## InMemoryEventJournal

An event journal in memory. Same trade-offs as `InMemoryStateStore`.

### method `AppendAsync(String, IReadOnlyList<Object>, Int64, CancellationToken)`

### method `DeleteToAsync(String, Int64, CancellationToken)`

### method `HighestSequenceAsync(String, CancellationToken)`

### method `ReadAsync(String, Int64, CancellationToken)`

### property `Streams`

Persistence ids that have at least one event.

## InMemorySnapshotStore

A snapshot store in memory. Same trade-offs as `InMemoryStateStore`.

### method `DeleteAsync(String, CancellationToken)`

### method `LoadAsync<T0>(String, CancellationToken)`

### method `SaveAsync<T0>(String, T0, Int64, CancellationToken)`

## InMemoryStateStore

State storage in a dictionary. Durable across deactivation and reactivation, which is what makes the virtual-actor lifecycle work, but not across a process restart.

The default for a reason: it makes the framework work out of the box and it makes tests fast. It is the wrong choice the moment the state matters - use `FileStateStore` or a database provider then.

### property `Count`

How many keys are stored. Useful in tests and on the dashboard.

### method `DeleteAsync(String, CancellationToken)`

### method `ReadAsync<T0>(String, CancellationToken)`

### method `WriteAsync<T0>(String, T0, Int64, CancellationToken)`

## JournalCompactor

Truncates a journal safely: never past what a snapshot covers, and never past a retention policy.

`DeleteToAsync` deletes what it is told to delete. It is the primitive, and it has no way to know whether a snapshot exists at that sequence - so getting the number wrong loses state, silently, and the loss only surfaces the next time that actor recovers, which can be weeks later on a node nobody was watching.

This checks first. It reads the snapshot, refuses to delete anything the snapshot does not cover, and applies a retention policy on top. It cannot make the pair atomic - the two stores are separate, and the journal has no transaction spanning them - but it fails in the safe direction: an interruption between the check and the delete leaves more history than needed, never less.

### method `JournalCompactor(IEventJournal, ISnapshotStore)`

Truncates a journal safely: never past what a snapshot covers, and never past a retention policy.

`DeleteToAsync` deletes what it is told to delete. It is the primitive, and it has no way to know whether a snapshot exists at that sequence - so getting the number wrong loses state, silently, and the loss only surfaces the next time that actor recovers, which can be weeks later on a node nobody was watching.

This checks first. It reads the snapshot, refuses to delete anything the snapshot does not cover, and applies a retention policy on top. It cannot make the pair atomic - the two stores are separate, and the journal has no transaction spanning them - but it fails in the safe direction: an interruption between the check and the delete leaves more history than needed, never less.

### method `CompactAsync<T0>(String, CompactionPolicy, CancellationToken)`

Compacts one stream, using the snapshot already stored for it.

- `persistenceId` — The stream to compact.
- `policy` — How much to keep. Defaults to `Aggressive`.
- `cancellationToken` — Cancellation.

### method `SnapshotAndCompactAsync<T0>(String, T0, Int64, CompactionPolicy, CancellationToken)`

Writes a snapshot and then compacts, which is the pair most callers want.

In this order, always. Truncating before the snapshot is stored is the one sequence that can lose an actor's state outright, and it is an easy thing to write by accident.

## JournalEntry

One appended event.

### method `JournalEntry(String, Int64, Object, DateTimeOffset)`

One appended event.

## PersistenceLog

Log messages the persistence base classes emit.

## PersistentActor&lt;T0&gt;

A virtual actor whose state is loaded on activation and written back on deactivation.

This is the Orleans-style grain state model. The actor works against `State` as a plain field; the runtime handles the reload, and because deactivation flushes, an actor that is swept for being idle - or handed to another node during a rebalance - comes back with what it had.

Write-on-deactivate is the default because it turns N updates into one store write. It also means a hard process kill loses everything since the last flush. Call `SaveStateAsync` after any change you are not willing to lose, or override `SaveEvery` to have the base class do it for you every N messages.

### method `ClearStateAsync(CancellationToken)`

Removes the stored state and resets `State` to a fresh instance.

### property `IsNew`

True when nothing was stored under this key and `State` is a fresh instance.

### method `OnActivateAsync(CancellationToken)`

Reads the state before the first message. Override and call base first.

### method `OnDeactivateAsync(DeactivationReason, CancellationToken)`

Flushes on the way out, unless the actor was stopped by a supervisor.

### method `OnStateLoadedAsync(CancellationToken)`

Runs after state is available, on every activation. The place to rebuild derived data.

### property `PersistenceKey`

The store key. Defaults to the actor's own address, which gives every actor its own row and is what makes reactivation on another node find the same state.

### method `ReceiveCoreAsync(Object, CancellationToken)`

Runs the message, then applies the `SaveEvery` checkpoint policy.

The checkpoint deliberately only runs when the handler returned normally. A message that threw is about to reach the supervisor, and its half-applied changes are not something to write down.

### property `SaveEvery`

Flush every this many messages, in addition to on deactivation. Zero - the default - flushes only on deactivation and on explicit `SaveStateAsync` calls.

### method `SaveStateAsync(CancellationToken)`

Writes the state now.

### property `State`

The actor's state. Never null after activation.

## ProjectionRunner

Runs a projection over one journal stream, from where it left off.

One stream at a time, and that is the honest shape of it. A projection over *every* event in the journal needs the journal to have a total order across streams, and `IEventJournal` has no such thing: sequences are per persistence id, so there is no position that means "everything before this, everywhere". Adding one means a column in every provider's schema and a reader that tolerates the gaps an auto-increment leaves when two writers commit out of order - a migration and a correctness problem, not an afternoon.

What this does cover is the common case it is usually wanted for: fold one aggregate's history into a view, catch up after a restart, rebuild from scratch on demand. Several streams means several runners, which is fine when the caller knows which streams it cares about.

### method `ProjectionRunner(IEventJournal, IStreamPositions)`

Runs a projection over one journal stream, from where it left off.

One stream at a time, and that is the honest shape of it. A projection over *every* event in the journal needs the journal to have a total order across streams, and `IEventJournal` has no such thing: sequences are per persistence id, so there is no position that means "everything before this, everywhere". Adding one means a column in every provider's schema and a reader that tolerates the gaps an auto-increment leaves when two writers commit out of order - a migration and a correctness problem, not an afternoon.

What this does cover is the common case it is usually wanted for: fold one aggregate's history into a view, catch up after a restart, rebuild from scratch on demand. Several streams means several runners, which is fine when the caller knows which streams it cares about.

### method `CatchUpAsync(IProjection, String, Int32, CancellationToken)`

Folds everything not yet seen, and returns how many events were applied.

- `projection` — The read model to bring up to date.
- `persistenceId` — The stream to fold.
- `checkpointEvery` — Events applied between writes of the position. Larger replays more after a crash and writes less while running.
- `cancellationToken` — Cancellation.

### method `PositionAsync(IProjection, String, CancellationToken)`

How far a projection has got through one stream.

### method `RewindAsync(IProjection, String, CancellationToken)`

Forgets where a projection had got to, so the next run folds the stream from the beginning.

The caller has to clear the read model itself. This only moves the position, and a runner that also emptied somebody else's table would be guessing at where it was.

## StateCloning

Deep-copies state on the way into and out of the in-memory stores.

Without this, an in-memory store holds a reference to the object the actor is still mutating, so a "stored" value keeps changing after it was stored. The bug that exposes it is subtle and specific: an event-sourced actor writes a snapshot at sequence 20, keeps applying events to the same instance, and recovery then reads a snapshot whose contents are from sequence 25 but whose sequence number says 20 - so the events after 20 get applied twice and the state is silently wrong.

A database provider gets this for free, because writing serializes. The in-memory stores have to do it deliberately, which is also what keeps them an honest stand-in for a real one in tests.

### method `Clone<T0>(T0)`

Returns a copy that shares no mutable state with `value`.

## StateConcurrencyException

Thrown when a write's expected version does not match what is stored - two writers raced for the same key.

The runtime keeps one activation per key per cluster, so this should not happen in normal operation. It shows up during the brief overlap while an actor hands off between nodes, and surfacing it is better than silently letting the loser overwrite the winner.

### method `StateConcurrencyException(String, Int64, Int64)`

Thrown when a write's expected version does not match what is stored - two writers raced for the same key.

The runtime keeps one activation per key per cluster, so this should not happen in normal operation. It shows up during the brief overlap while an actor hands off between nodes, and surfacing it is better than silently letting the loser overwrite the winner.

## StateSnapshot&lt;T0&gt;

A snapshot of an event-sourced actor's state at a sequence number.

### method `StateSnapshot<T0>(T0, Int64, DateTimeOffset)`

A snapshot of an event-sourced actor's state at a sequence number.

## StoredState&lt;T0&gt;

A stored value together with the version it was read at.

### method `StoredState<T0>(T0, Int64)`

A stored value together with the version it was read at.

---

[Back to the index](README.md)
