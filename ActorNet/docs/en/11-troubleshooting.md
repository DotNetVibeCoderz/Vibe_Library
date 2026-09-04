# Troubleshooting

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[Bahasa Indonesia](../id/11-pemecahan-masalah.md) · [Docs index](README.md)*

## `ActorTypeNotRegisteredException`

```
Actor type 'BankAccountActor' is not registered. Call RegisterActor<BankAccountActor>()
on the actor system before sending to it.
```

Exactly what it says. The type half of an address is resolved through an explicit registry, never
by scanning assemblies — so every actor type must be registered before anything is sent to it, on
**every node** that might own its keys.

This throws rather than dropping the message on purpose: an unregistered type is a wiring bug, and
a silently discarded message just turns it into a hang somewhere else.

## `AskTimeoutException`

```
No reply from 'BankAccountActor/alice' within 10,000 ms. The actor may not call ReplyAsync
for this message type.
```

In order of likelihood:

1. **The handler never replies.** Ask requires `Context.ReplyAsync`. A handler with no matching
   case replies to nothing.
2. **The mailbox is long.** An ask is ordered behind everything already queued. If the actor is
   30,000 messages behind, the reply waits for all of them. Check the mailbox depth in the console.
3. **The handler is slow or blocked.** A synchronous blocking call inside `ReceiveAsync` holds the
   actor's only thread of control.
4. **A remote node is unreachable.** The reply cannot get back. Check the cluster page.

Note what it is *not*: if the handler **throws**, you get an `ActorNetException` carrying the
original as its inner exception, not a timeout. A timeout means nothing came back at all.

## `AskReplyTypeMismatchException`

```
Actor 'CounterActor/x' replied with Pong but the caller asked for Total.
```

A caller bug, and deliberately not a timeout — the reply did arrive, it was just not what was asked
for.

## `UnknownMessageTypeException`

```
No message type is registered under the alias 'Bank.Deposit'. Register it with
RegisterMessage<T>() on both nodes.
```

Every message crossing a node boundary needs a registered alias on **both** ends. Usually one of:

- The type is registered on the sender but not the receiver.
- The alias differs — `[ActorMessage(Alias = "bank.deposit")]` on one side, the default full type
  name on the other.
- An external client is using an alias the node does not know.

Aliases default to the full type name, so two nodes running the same assemblies agree with no
configuration. Set an explicit alias as soon as a non-.NET client is involved.

This is an allow-list, not a lookup. There is no fallback to `Type.GetType`, by design.

## An actor keeps restarting

Check the console's **Restarts** column, or the log:

```
Restarted BankAccountActor/alice after InvalidOperationException (restart #7).
```

Then:

```
BankAccountActor/alice exceeded its restart budget (10 in 00:01:00); stopping instead of restarting.
```

That second line is the budget working. Without it the actor would restart forever on the same
message and burn a core.

Usually it is a **poison message**: something at the head of the mailbox that fails identically
every time. Change the strategy to `Resume` for that exception class so the bad message is dropped,
or fix the handler to reject it rather than throw.

## State is lost after a restart

Expected, for an in-memory actor: a restart builds a fresh instance. That is what the supervision
sample demonstrates.

If you did not expect it, derive from `PersistentActor<TState>` or `EventSourcedActor<TState>`.

## State is lost after a process restart

Expected, with the default in-memory store: it survives deactivation, not a process restart.

```csharp
options.StateStore    = new FileStateStore("./data/state");
options.EventJournal  = new FileEventJournal("./data/journal", types);
options.SnapshotStore = new FileSnapshotStore("./data/snapshots");
```

Or `--data ./data` on the CLI.

## State is lost after a rebalance

The default stores are per-process. When an actor's key moves to another node, it reactivates there
and finds nothing.

A real cluster needs a store both nodes can read — a database provider, which is not built yet.
Until then, cluster only actors whose state can be rebuilt.

## A node shows as `Unreachable`

Missed heartbeats. An unreachable member **stays on the ring**, because the usual cause is a GC
pause or a blip and moving its keys costs a wave of deactivations.

If it stays unreachable, check:

- The node's advertised `Host`/`Port` are reachable *from the peer*, not just bound locally.
- A firewall between them.
- `HeartbeatInterval` against `UnreachableAfter` — the validator refuses the obviously wrong
  combinations, but a heavily loaded node can still miss beats.

## A seed node is marked unreachable even though it is healthy

The first node of a cluster has no seeds of its own, so `Cluster.Enabled` stays false unless you
say otherwise — and a node with clustering off answers a join handshake but never gossips. Peers
then time it out.

```bash
actornet run --node-id node-a --port 9000 --cluster
```

```csharp
options.Cluster.Enabled = true;
options.Cluster.Seeds = [];   // no seeds; others join this node
```

## Two nodes disagree about who owns a key

Should not happen — the hash is process-independent and the ring is built from a sorted,
de-duplicated member list. If it does:

- **The member tables differ.** Check both cluster pages; they may not have converged yet.
- **`VirtualNodesPerMember` differs between nodes.** It must be the same everywhere.
- **Two nodes share a `NodeId`.** It must be unique and stable.

## Memory grows without bound

Two usual causes.

**Unbounded mailboxes with a slow consumer.** The default mailbox is unbounded, which is what makes
a tell cheap and what lets a firehose fill the heap. Bound it:

```csharp
options.MailboxCapacity = 10_000;
```

Watch the **In flight** figure in the console — climbing means work is arriving faster than it
retires.

**Actors that never go idle.** The sweeper only stops actors idle past `IdleTimeout` with an empty
mailbox. An actor receiving a message every second never qualifies. If you have millions of those,
you need more nodes, not a shorter timeout.

## `dotnet test` fails with MSB4025

Expected here. The .NET 10 SDK dropped the VSTest bridge that xunit.v3's Microsoft.Testing.Platform
runner needs. The test project is its own executable:

```bash
dotnet run --project tests/ActorNet.Tests -c Release
```

`dotnet.config` in the repository root exists for this. Do not delete it assuming it is unused.

## The console shows nothing

- No actors are activated. An actor appears only after its first message. Start the console with
  `ActorNet:GenerateLoad=true`, or send something.
- The console shows **one node** — its own. Counters and the actor list are local; only the ring
  and membership are cluster-wide. Cross-node aggregation is on the roadmap.

## Getting more detail

```bash
actornet run -v                # debug-level runtime logging
ACTORNET_DEBUG=1 actornet ...  # full stack traces from the CLI
```

At debug level the runtime logs an activation and a deactivation for every actor, which on a busy
node is thousands of lines a second — which is why it is not the default.

## Next

- [Supervision](04-supervision.md)
- [Clustering](06-clustering.md)
- [Roadmap](../../Plan.md) — what is known to be missing
