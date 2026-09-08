# ActorNet.Metrics

Generated - edit the `///` comments in the source, not this file.

## ActorNetDiagnostics

The OpenTelemetry surface: one `ActivitySource` and one `Meter`.

The runtime's own counters feed the console, which is fine for looking at one node and useless for anything that aggregates. These are the standard .NET primitives, so an application already exporting telemetry picks them up by adding the names below - ActorNet does not take a dependency on OpenTelemetry to provide them.

Nothing here costs anything when nobody is listening. `StartActivity` returns null with no listener, and an `Instrument` with no collector does not record. That is why tracing can sit on the per-message path at all.

### property `Activations`

Actor activations.

### field `ActivitySourceName`

The name to add to a tracer provider.

### property `Congestion`

Sends refused because a peer's outbound queue never made room, tagged with the node.

Worth alerting on separately from dead letters. A dead letter usually means a wiring mistake; congestion means the cluster is carrying more than a peer can take, which is a capacity signal rather than a bug.

### property `Deactivations`

Actor deactivations, tagged with the reason.

### property `DeadLetters`

Messages that could not be delivered, tagged with the reason.

### property `MessagesFailed`

Messages whose handler threw.

### property `MessagesProcessed`

Messages handled successfully.

### property `Meter`

Instruments for throughput, failures and latency.

### field `MeterName`

The name to add to a meter provider.

### property `ProcessingDuration`

How long a handler took.

### property `QueueLatency`

How long a message waited in a mailbox before being handled.

The more useful of the two histograms for capacity work. Handler time says how expensive the work is; queue time says whether the node is keeping up with it.

### property `Restarts`

Actor restarts ordered by a supervisor.

### property `Source`

Spans for message handling.

### method `StartReceive(ActorId, String, String, String)`

Starts a span for one message, or returns null when nothing is listening.

- `traceParent` — W3C `traceparent` from the sending node, for a message that crossed the wire. Without it the receiving span starts a trace of its own, and one operation spanning three machines reads as three unrelated traces.
- `traceState` — W3C `tracestate`, passed on unchanged.

`Consumer` because handling a message is consuming from a queue, which is what makes a trace viewer draw it as the receiving half of a send.

### property `Version`

Version reported alongside both, taken from the assembly.

## ActorSnapshot

A point-in-time reading of one actor.

### method `ActorSnapshot(String, String, DateTimeOffset, DateTimeOffset, Int64, Int64, Int32, Int32, Double)`

A point-in-time reading of one actor.

### property `Age`

How long this actor has been activated.

### property `Idle`

How long since it last handled anything - what the idle sweeper measures against.

## ActorSystemSnapshot

A point-in-time reading of the whole node.

- `MessagesDispatched` — Messages accepted for handling on this node - local sends and inbound remote frames alike, but not messages this node forwarded to whichever peer owns their key.

### method `ActorSystemSnapshot(String, DateTimeOffset, TimeSpan, Int64, Int64, Int64, Int64, Int64, Int64, Int64, Int64, Int64, Int64, Int64, Int32, Int32, Double, Double, IReadOnlyList<ActorSnapshot>)`

A point-in-time reading of the whole node.

- `MessagesDispatched` — Messages accepted for handling on this node - local sends and inbound remote frames alike, but not messages this node forwarded to whichever peer owns their key.

### property `InFlight`

Messages accepted for handling on this node but not yet processed. A number that keeps climbing is the signal that the node is taking work faster than it retires it.

`MessagesDispatched` counts only what this node took on itself, so a message forwarded to the node that owns its key is not in flight here. Counting forwarded messages would make this climb forever on any node that routes remotely.

### property `MessagesDispatched`

Messages accepted for handling on this node - local sends and inbound remote frames alike, but not messages this node forwarded to whichever peer owns their key.

### property `MessagesPerSecond`

Sustained processing rate since the node started.

## ClusterStatus

What every node in the cluster is doing, as far as this one could find out.

The ring and the member table have always been cluster-wide; the counters were not, so the console could tell you the cluster had five members and only what one of them was doing. A node under load looks identical to an idle one from four nodes away.

`Silent` is part of the answer, not an error. A peer that did not reply in time is a fact about the cluster worth showing next to the ones that did - summing four nodes and presenting it as five would be worse than saying which one is missing.

### method `ClusterStatus(DateTimeOffset, IReadOnlyList<NodeStatus>, IReadOnlyList<String>)`

What every node in the cluster is doing, as far as this one could find out.

The ring and the member table have always been cluster-wide; the counters were not, so the console could tell you the cluster had five members and only what one of them was doing. A node under load looks identical to an idle one from four nodes away.

`Silent` is part of the answer, not an error. A peer that did not reply in time is a fact about the cluster worth showing next to the ones that did - summing four nodes and presenting it as five would be worse than saying which one is missing.

### property `ActiveActors`

Actors currently activated across the cluster.

### property `Busiest`

The node carrying the most in-flight work, or null when nothing answered.

The number an operator actually wants from an aggregate. A cluster total hides the case this exists to find: one node buried while the others idle, which is a placement problem rather than a capacity one.

### property `DeadLetters`

Undelivered messages across the cluster.

### property `InFlight`

Messages accepted and not yet retired, across the cluster.

### property `MailboxDepth`

Mailbox depth across the cluster, which is where a struggling node shows first.

### property `MessagesFailed`

Handlers that threw, across every node that answered.

### property `MessagesProcessed`

Messages handled across every node that answered.

## IMetricsCollector

Read side of the metrics the runtime keeps.

### method `Snapshot(Boolean)`

Takes a consistent-enough reading of the node. Cheap enough to poll once a second.

## MetricsCollector

The runtime's counters. Every write is a single interlocked operation on the hot path; nothing here takes a lock, and nothing allocates until `Snapshot` is called.

A snapshot is not a transactional view - counters are read one after another while the node keeps running, so `InFlight` can read slightly negative in principle and is clamped. That is the right trade: an exact reading would mean stopping the world for a dashboard poll.

### method `RecordProcessed(ActorMetrics, Int64, Int64)`

Records a successfully handled message, folding its timings into both the actor and the node.

### method `RegisterActor(ActorId, String, Func<Int32>)`

Registers an actor so it shows up in snapshots. Called once per activation.

### method `Snapshot(Boolean)`

### method `UnregisterActor(ActorId)`

Removes an actor from snapshots. Called once per deactivation.

## NodeStatus

One node's counters, small enough to ask every member for on a refresh.

Deliberately not `ActorSystemSnapshot`. That carries a row per active actor, which is the right thing for the node you are standing on and the wrong thing to pull from twenty peers every second - the answer would be larger than everything else on the wire put together.

### method `NodeStatus(String, TimeSpan, Int64, Int64, Int64, Int32, Int32, Int64, Int64, Int64, Double, Double)`

One node's counters, small enough to ask every member for on a refresh.

Deliberately not `ActorSystemSnapshot`. That carries a row per active actor, which is the right thing for the node you are standing on and the wrong thing to pull from twenty peers every second - the answer would be larger than everything else on the wire put together.

### method `From(ActorSystemSnapshot)`

Reduces a full snapshot to what is worth sending.

---

[Back to the index](README.md)
