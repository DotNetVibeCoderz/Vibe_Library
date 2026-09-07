# ActorNet — development tracking

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

What is built, what is not, and what is known to be missing from the things that are. For where the
project is headed, see [Plan.md](Plan.md) — the product roadmap.

This list is meant to be honest rather than flattering. **A box is ticked only when the feature
works and something automated proves it** — not when the code exists.

---

## Core runtime

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
- [x] Bounded inbound delivery, so one full mailbox cannot stall a connection's other actors
- [x] Congestion, outage, refusal and ask timeout are four distinct, diagnosable failures
- [x] `SendTimeout` and `OutboundQueueCapacity` for the per-peer send queue
- [x] Dead-letter queue for undeliverable messages, bounded, with a subscription
- [x] `[ActorInterface]`: request and reply records, a proxy and an actor base, generated and
      checked at compile time
- [x] Message priority: `[Urgent]` on a message type, a second mailbox lane created on demand,
      with a burst limit so urgent traffic cannot starve the ordinary lane

## Supervision

- [x] `Resume`, `Restart`, `Stop`, `Escalate`
- [x] One-for-one and all-for-one scope
- [x] Restart budget (`MaxRestarts` within `Window`)
- [x] Per-type strategies, inherited by children
- [x] Supervision trees via `SpawnChild`; children stop with their parent
- [x] Escalation to the root guardian
- [x] A supervision stop does not flush half-updated persistent state
- [x] Exponential backoff with jitter between restarts, the first one still immediate
- [x] Watch/`Terminated` notifications between actors, across nodes, for the two stops that
      mean something rather than all five

## Clustering

- [x] Seed-based join handshake
- [x] Gossip membership with incarnation numbers
- [x] Failure detection with unreachable distinct from down
- [x] Consistent hashing with virtual nodes
- [x] Process-independent hash, verified against pinned vectors
- [x] Automatic rebalancing on membership change
- [x] Graceful leave, flushing state before the departure is announced
- [x] Warm handoff: a leaving node asks each successor to activate the keys it inherits
- [x] Coordinating a rolling restart: one leave token, handed out by the lowest node id, with a
      lease so a killed holder does not wedge the cluster and a bounded wait so a shutdown cannot
      hang. Opt-in (`--coordinated-leave`); orders shutdowns in a healthy cluster, and is not
      consensus - see the gaps below
- [x] `--cluster` for the first node, which has no seeds of its own
- [x] Nodes across machines, bound to a routable address - three machines, two operating systems
      and two processor architectures in one cluster
- [x] The seed join is retried while a node is alone, so nodes may start in any order
- [x] Seeds contacted concurrently and bounded by `JoinTimeout`, so a black-holed seed neither
      starves the others nor holds up startup
- [x] Hostname advertising for containers - binds all interfaces, advertises the name
- [x] `AdvertisedHost` / `AdvertisedPort` separate from the bind address, with startup validation
- [x] Phi-accrual failure detection, adaptive per peer and the default
- [x] Fixed deadlines kept as an option, for when a stated number beats an accurate one
- [x] Gossip fanout, taken in rotation so the interval between two nodes stays learnable
- [x] Split-brain resolution - `KeepMajority` and `StaticQuorum`, opt-in, the losing side stops
- [x] `--split-brain` and `--quorum` on the CLI
- [x] A denominator both halves share: the agreed membership is gossiped with an epoch, raised
      only by a node that can see the whole cluster, so two sides cannot both be a majority
- [x] `PreferenceList` has a user: a departing node finds each key's successor with it
- [ ] Eagerly activating inherited keys after an unplanned loss; the survivors do not know
      which keys the dead node held
- [x] Seed names resolved to every address behind them, for a Kubernetes headless service
- [ ] Watching the Kubernetes API for members, rather than resolving a name each attempt

## Persistence

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

## Networking

- [x] Length-prefixed framing (correct across coalesced and split reads)
- [x] One persistent connection per peer, with reconnect and capped backoff
- [x] Serialized writes per connection
- [x] Explicit type allow-list — an unregistered alias is refused, never resolved by name
- [x] Frame size cap against hostile input
- [x] Replies to non-member clients over their inbound connection
- [x] TLS between nodes (1.2/1.3), with thumbprint pinning and optional mutual TLS
- [x] Authentication between nodes - HMAC challenge-response, the secret never sent
- [x] A binary envelope encoding, chosen per node and detected per frame
- [x] A binary message body, tagged by position so an unknown field can be skipped, with a
      per-type fallback to JSON for shapes it does not cover

## Streams

- [x] `Where`, `Select`, `SelectAsync`, `Take`, `Batch`, `Buffer`, `Tap`
- [x] Routing into actors by key
- [x] Producer failures propagate through a buffer
- [ ] Merge, split, and fan-in operators
- [ ] Durable stream positions across a restart

## Diagnostics

- [x] Dead letters recorded at every drop point, with a reason and the message where it exists
- [x] `ActivitySource` spans per handled message, marked as errors when the handler throws
- [x] `Meter` counters and histograms - throughput, failures, lifecycle, handler and queue time
- [x] Dead letters in the console and on the read-only API
- [x] Trace context propagated across a node hop, W3C `traceparent` on every frame

## Tooling and surfaces

- [x] CLI: `run`, `monitor`, `demo`, `cluster`, `bench`, `scenarios`
- [x] CLI packed as a dotnet tool (`actornet`)
- [x] Blazor Server console with live vitals, actor browser, cluster view
- [x] Placement ring visualisation with a key probe
- [x] Read-only HTTP API (`/api/metrics`, `/api/cluster`)
- [x] Avalonia desktop samples — banking, telemetry, ordering, supervision
- [x] BenchmarkDotNet suite for messaging and routing
- [x] `ActorNet.AspNetCore`: a cluster-aware health check, read-only diagnostics endpoints, and a
      readiness filter
- [x] Cross-node metrics aggregation - every peer asked for its own counters, silent ones named
- [x] Actor inspector — read an actor's state without writing a query message; on the actor's own
      loop, routed by the ring, `IInspectable` for actors that would rather not show everything

## Clients

- [x] C# (`ActorNet.Client.ActorNetClient`) — verified in the test suite
- [x] Node.js — verified against a live node
- [x] Python — verified against a live node
- [x] Go — compiles and is exercised in CI; not run on the machine it was written on
- [x] Reconnect and failover to another node - C# client, several endpoints tried in rotation
- [x] The same for the Node.js, Python and Go clients, each checked in CI against a dead
      endpoint ahead of a live one
- [ ] Cluster-aware routing in clients (they connect to one node and let it forward)

## Testing

- [x] 197 tests: 137 run everywhere, 60 skip unless a database server is reachable
- [x] Addressing, hashing, and ring segment properties
- [x] Concurrency: 6,400 increments with no lock, no update lost
- [x] Supervision outcomes and the restart budget
- [x] Persistence and event-sourced replay, including "did not flush a failed actor"
- [x] Framing across coalesced, split, oversized and torn frames
- [x] Two-node cluster: convergence, placement agreement, remote tell, remote ask, rebalance
- [x] External client tell, ask, concurrent asks, failure, timeout, and allow-list refusal
- [x] Fault injection - a node's transport pulled out from under it, then asserting on
      detection, inheritance of its keys, and a replacement rejoining at the same address
- [ ] Long-running soak test

## Documentation

- [x] Bilingual README (English and Bahasa Indonesia)
- [x] `docs/en/` and `docs/id/`, parallel and cross-linked
- [x] Screenshots of the console and the desktop samples — real captures, not mockups
- [x] `clients/README.md` covering the protocol and what a client can and cannot do
- [x] `CLAUDE.md` for future contributors
- [ ] API reference generated from the XML docs

## Release

- [x] CI: build and test on Linux, Windows and macOS
- [x] CI: all four clients driven against a real node
- [x] CI: every persistence provider run against a real server, with a guard that fails the job if one was skipped
- [x] Publish workflow, tag-triggered (`ActorNet-v*`), with a dry-run mode
- [x] NuGet metadata pointing at the subfolder, SourceLink, symbol packages
- [x] Published to nuget.org - 8 packages at 0.1.0
- [x] Published to npm (`actornet-client`, with type definitions) and PyPI (`actornet`)
- [x] A tagged release: `ActorNet-v0.4.0` published all nine packages through the workflow,
      verified by installing `ActorNet` from nuget.org into a project that uses a generated proxy

---

## Known gaps in what is ticked

Ticking a box means it works, not that it is finished. These are the caveats worth carrying:

- **One intermittent test failure is still unexplained.**
  `WarmHandoffTests.TheSuccessorHasTheActorsBeforeTheFirstMessageArrives` fails roughly one run in
  twenty, always by exhausting the fifteen-second wait for the successor to hold the actors it
  inherited. Measurement has ruled out the obvious causes: the warm frames are always sent, always
  arrive, and always activate on the successor. Hunting it did turn up a real defect - a node whose
  ring still held the departing node would deactivate the actors it had just inherited the moment
  the failure detector marked that node unreachable - and that is fixed, and covered by
  `AnActorIsNotHandedToAnOwnerNobodyCanReach`, which fails deterministically without the fix. But
  the flake recurred after that fix, so the two are not the same thing. The assertion now names the
  actors that were missing, so the next occurrence says whether a warm frame never arrived or an
  arrived one was deactivated again.
- **A frame queued for a peer can still be dropped at shutdown.** `PeerConnection.DisposeAsync`
  cancels its writer loop before the loop drains the queue. The leave announcement and the warm
  handoff are both written straight to a socket to sidestep this, because both are sent moments
  before the transport closes; everything else still relies on the loop winning the race, which it
  ordinarily does. The right fix is to drain before cancelling, and it has not been made.
- **The leave token is not a distributed lock.** It lives in the coordinator's memory, so a change
  of coordinator forgets who held it, and in a partition each side has a coordinator and each will
  grant. It orders the shutdowns of a healthy cluster, which is what a rolling restart is. Nothing
  here defends against a partition; `SplitBrainStrategy` is what does that.
- **The console's actor list is still one node's.** Counters are now collected from every peer;
  the per-actor table is not, and pulling a row per actor from twenty peers on a refresh would cost
  more than everything else on the wire put together.
- **A node's member table is updated before its ring is rebuilt from it.** For that moment a node
  reports the right membership and still routes by the old ring, which is why tests wait on the
  ring rather than the table. Nothing in the runtime depends on the two being in step, but anything
  that reads membership to predict placement does.
- **The generated proxies have no benchmark.** They emit the same calls a hand-written ask makes,
  so there is no reason to expect a difference, and "no reason to expect" is not a measurement.
- **The gossip fanout has only been reasoned about, not measured.** A rotation of four peers per
  beat converges in about log(members) rounds on paper; the largest cluster ever run here is four
  nodes, which is below the fanout and therefore still a full mesh.
- **The Go client has never run** on the machine it was written on. CI compiles it and drives it
  against a node; that is the only evidence it works.
- **The throughput figures in the docs no longer match this machine.** Measured 2026-09-07 with
  `bench -n 2000000`, alternating between builds: about 1.9M msg/s drained and 270 B/msg, against
  the 3.2-3.6M msg/s and 160-180 B/msg the docs quote. The unmodified build measures the same, so
  it is not from any recent change; whether the machine has changed underneath the number or
  something regressed earlier has not been established, and the prose has deliberately not been
  edited to match a figure nobody has explained.
- **The benchmark is in-process.** No network hop, no persistence, and the handler does nothing
  but increment. It measures the runtime's floor, not an application's throughput.
- **Rebalancing deactivates rather than migrates.** An actor whose key moves is flushed and
  reactivated from the store on its new owner, so an actor with no persistent state loses it.
- **A cluster has now run across three machines, but never in containers.** Windows x64, macOS 15
  on Intel and macOS 13 on Apple Silicon joined one cluster over a wireless LAN, agreed on the same
  three-member table, and routed tells and asks between them. Docker and Kubernetes remain
  unexercised, and nothing has been run across a WAN or a NAT boundary.
- **Four of the seven persistence providers have never run on a developer machine here** - there is
  no Docker on it. PostgreSQL, SQL Server, MySQL and Redis are exercised by the CI job against
  service containers, and that job fails if any of their tests were skipped. SQLite and the two
  built-in stores run on every local test run.
