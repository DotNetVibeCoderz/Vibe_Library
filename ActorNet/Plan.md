# ActorNet — roadmap and tracking

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

Two halves. **Part one** is the product roadmap: where this is going and why. **Part two** is the
development checklist: what is actually built, what is not, and what is known to be missing from
the things that are.

The checklist is meant to be honest rather than flattering. A box is ticked only when the feature
works and something automated proves it.

---

## Part one — product roadmap

### The thesis

Orleans makes distributed systems approachable by hiding the lifecycle: you address an actor and it
is there. Akka.NET makes them survivable by exposing it: you decide what happens when one fails.
Most teams end up wanting both and picking one.

ActorNet's bet is that the two are not actually in tension. A virtual actor can have a supervision
strategy. A grain can be event-sourced. The lifecycle can be automatic *and* the failure policy
explicit, because they answer different questions — "when does this exist?" and "what happens when
it breaks?"

### Where it stands

Version 0.1 is a working framework, not a prototype: virtual actors, supervision, clustering with
consistent hashing, two persistence models, reactive streams, a console, desktop samples, and four
client SDKs. It is not yet a framework anyone should run a bank on, and the roadmap below is mostly
about closing that gap.

### 0.2 — durability and operability

The things that stand between this and a production pilot.

| Theme | Why it matters |
| --- | --- |
| Backpressure that reaches the sender | A bounded mailbox blocks the local sender today, but a remote sender only sees a full transport queue. |
| Structured diagnostics (`ActivitySource`, `Meter`) | The console reads the runtime's own counters; nothing exports to OpenTelemetry yet. |
| Dead letters | An undeliverable message is logged and dropped. It should be observable and re-drivable. |
| Ask over a bounded transport queue | A slow peer currently makes an ask time out with no way to distinguish it from a slow actor. |

### 0.3 — clustering that survives a bad day

The membership layer is deliberately simple, and simple has limits worth being explicit about.

| Theme | Why it matters |
| --- | --- |
| Phi-accrual failure detection | A fixed deadline calls a GC pause a failure. An adaptive detector does not. |
| Gossip fanout limits | Every node gossips to every node: fine at tens, quadratic past that. |
| Split-brain resolution | Two halves of a partitioned cluster each believe they own the whole ring. Today nothing arbitrates. |
| Replica placement | `PreferenceList` exists and nothing uses it. Standby replicas would make a node loss invisible. |
| Rolling upgrade | Handing off a node's actors before it stops, rather than deactivating them and waiting for traffic. |

### 0.4 — ecosystem

| Theme | Why it matters |
| --- | --- |
| Source-generated actor proxies | `AskAsync<Balance>(id, new GetBalance())` could be `account.GetBalanceAsync()`, checked at compile time. |
| ASP.NET Core integration package | Endpoint filters and health checks that know about actors. |
| Kubernetes discovery | Seeds are static strings. A headless service should be enough. |
| A binary wire format | JSON is the right default and the wrong choice for a hot inter-node path. |

### Deliberately not planned

- **Exactly-once delivery.** At-most-once with an idempotent handler is the honest primitive.
  Promising more would mean a transaction log on every send.
- **Actor migration with in-flight state.** Deactivate-and-reactivate through the store is
  simpler, and it is what makes an actor's state durable rather than merely resident.
- **A DSL for supervision trees.** Registration plus `SpawnChild` covers it. A DSL would be
  something new to learn for no new capability.

---

## Part two — development checklist

### Core runtime

- [x] `ActorId` addressing, `Type/Key`, hierarchical keys
- [x] Virtual actors — activation on first message, no explicit creation
- [x] Mailboxes on `System.Threading.Channels`, bounded or unbounded
- [x] Single-threaded-per-actor guarantee (no locks in application actors)
- [x] Activation completes before the first message is handled
- [x] Idle sweeper with a configurable timeout
- [x] `Tell` and `Ask`, with typed replies and timeouts
- [x] A failing handler answers its pending ask with the failure rather than a timeout
- [x] `ReceiveActor` for type-dispatched handlers
- [x] Constructor injection through `IServiceProvider`
- [x] Synchronous fast path for the common send
- [ ] Dead-letter queue for undeliverable messages
- [ ] Message priority or a second mailbox lane

### Supervision

- [x] `Resume`, `Restart`, `Stop`, `Escalate`
- [x] One-for-one and all-for-one scope
- [x] Restart budget (`MaxRestarts` within `Window`)
- [x] Per-type strategies, inherited by children
- [x] Supervision trees via `SpawnChild`; children stop with their parent
- [x] Escalation to the root guardian
- [x] A supervision stop does not flush half-updated persistent state
- [ ] Backoff between restarts (they are currently immediate)
- [ ] Watch/`Terminated` notifications between actors

### Clustering

- [x] Seed-based join handshake
- [x] Gossip membership with incarnation numbers
- [x] Deadline failure detection, unreachable distinct from down
- [x] Consistent hashing with virtual nodes
- [x] Process-independent hash, verified against pinned vectors
- [x] Automatic rebalancing on membership change
- [x] Graceful leave
- [x] `--cluster` for the first node, which has no seeds of its own
- [ ] Phi-accrual failure detection
- [ ] Split-brain resolution
- [ ] Replica placement using `PreferenceList`
- [ ] Non-static seed discovery (DNS, Kubernetes)

### Persistence

- [x] `PersistentActor<TState>` — grain state, loaded on activation, flushed on deactivation
- [x] `EventSourcedActor<TState>` — journal, replay, snapshots
- [x] Optimistic concurrency, with `StateConcurrencyException` on a stale write
- [x] In-memory stores (default; survive deactivation, not a restart)
- [x] File-backed stores (survive a restart; JSON per key, JSONL per stream)
- [x] Persisting during recovery is refused
- [x] One conformance suite, run against every provider
- [x] SQLite provider - runs on every test run
- [x] PostgreSQL provider - verified in CI against a real server
- [x] SQL Server provider - verified in CI against a real server
- [x] MySQL / MariaDB provider - verified in CI against a real server
- [x] Redis provider - verified in CI against a real server
- [x] Schema exposed for a migration tool instead of auto-creation
- [ ] Journal compaction beyond `DeleteToAsync`
- [ ] Projections / read-model subscriptions off the journal

### Networking

- [x] Length-prefixed framing (correct across coalesced and split reads)
- [x] One persistent connection per peer, with reconnect and capped backoff
- [x] Serialized writes per connection
- [x] Explicit type allow-list — an unregistered alias is refused, never resolved by name
- [x] Frame size cap against hostile input
- [x] Replies to non-member clients over their inbound connection
- [ ] TLS
- [ ] Authentication between nodes
- [ ] Binary serialization option

### Streams

- [x] `Where`, `Select`, `SelectAsync`, `Take`, `Batch`, `Buffer`, `Tap`
- [x] Routing into actors by key
- [x] Producer failures propagate through a buffer
- [ ] Merge, split, and fan-in operators
- [ ] Durable stream positions across a restart

### Tooling and surfaces

- [x] CLI: `run`, `monitor`, `demo`, `cluster`, `bench`, `scenarios`
- [x] CLI packed as a dotnet tool (`actornet`)
- [x] Blazor Server console with live vitals, actor browser, cluster view
- [x] Placement ring visualisation with a key probe
- [x] Read-only HTTP API (`/api/metrics`, `/api/cluster`)
- [x] Avalonia desktop samples — banking, telemetry, ordering, supervision
- [x] BenchmarkDotNet suite for messaging and routing
- [ ] Cross-node metrics aggregation in the console (it shows one node)
- [ ] Actor inspector — read an actor's state without writing a query message

### Clients

- [x] C# (`ActorNet.Client.ActorNetClient`) — verified in the test suite
- [x] Node.js — verified against a live node
- [x] Python — verified against a live node
- [x] Go — compiles and is exercised in CI; not run on the machine it was written on
- [ ] Reconnect and failover to another node
- [ ] Cluster-aware routing in clients (they connect to one node and let it forward)

### Testing

- [x] 197 tests: 137 run everywhere, 60 skip unless a database server is reachable
- [x] Addressing, hashing, and ring segment properties
- [x] Concurrency: 6,400 increments with no lock, no update lost
- [x] Supervision outcomes and the restart budget
- [x] Persistence and event-sourced replay, including "did not flush a failed actor"
- [x] Framing across coalesced, split, oversized and torn frames
- [x] Two-node cluster: convergence, placement agreement, remote tell, remote ask, rebalance
- [x] External client tell, ask, concurrent asks, failure, timeout, and allow-list refusal
- [ ] Fault injection — killing a node mid-flight and asserting on recovery
- [ ] Long-running soak test

### Documentation

- [x] Bilingual README (English and Bahasa Indonesia)
- [x] `docs/en/` and `docs/id/`, parallel and cross-linked
- [x] Screenshots of the console and the desktop samples — real captures, not mockups
- [x] `clients/README.md` covering the protocol and what a client can and cannot do
- [x] `CLAUDE.md` for future contributors
- [ ] API reference generated from the XML docs

### Release

- [x] CI: build and test on Linux, Windows and macOS
- [x] CI: all four clients driven against a real node
- [x] CI: every persistence provider run against a real server, with a guard that fails the job if one was skipped
- [x] Publish workflow, tag-triggered (`ActorNet-v*`), with a dry-run mode
- [x] NuGet metadata pointing at the subfolder, SourceLink, symbol packages
- [ ] Published to nuget.org (the workflow is ready; nothing has been pushed)
- [ ] A tagged release

---

## Known gaps in what is ticked

Ticking a box means it works, not that it is finished. These are the caveats worth carrying:

- **The console shows one node.** Counters and the actor list are local. The ring and membership
  are cluster-wide, but there is no aggregation across nodes.
- **Membership is O(members²) per heartbeat.** Fine at tens of nodes. It needs a fanout limit
  before it is not.
- **The Go client has never run** on the machine it was written on. CI compiles it and drives it
  against a node; that is the only evidence it works.
- **The benchmark is in-process.** No network hop, no persistence, and the handler does nothing
  but increment. It measures the runtime's floor, not an application's throughput.
- **Rebalancing deactivates rather than migrates.** An actor whose key moves is flushed and
  reactivated from the store on its new owner, so an actor with no persistent state loses it.
- **Four of the seven persistence providers have never run on a developer machine here** - there is
  no Docker on it. PostgreSQL, SQL Server, MySQL and Redis are exercised by the CI job against
  service containers, and that job fails if any of their tests were skipped. SQLite and the two
  built-in stores run on every local test run.
