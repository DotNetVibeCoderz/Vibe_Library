# Performance

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[Bahasa Indonesia](../id/10-performa.md) · [Docs index](README.md)*

Every number here was measured on the machine described below. Nothing is extrapolated, and the
section at the end says plainly what these figures do **not** show.

## The machine

```
Intel Core i7-8650U, 1.90 GHz (Kaby Lake R) — 4 physical cores, 8 logical
Windows 11 (10.0.26200)
.NET SDK 10.0.400, runtime 10.0.11, X64 RyuJIT x86-64-v3
```

A laptop CPU. Server hardware will do considerably better; the *ratios* are the durable part.

## Message throughput

`actornet bench -n 2000000 -a 8`, three runs:

| Run | Drained | Allocated |
| --- | --- | --- |
| 1 | 3,596,089 msg/s | 160 B/msg |
| 2 | 3,407,405 msg/s | 166 B/msg |
| 3 | 3,166,185 msg/s | 181 B/msg |

**3.2–3.6M messages/second**, 8 actors, 8 sender tasks, 2M messages. The spread between runs is
larger than most micro-optimisations, which is worth knowing before chasing one.

### Why "drained" and not "dispatched"

The benchmark reports both:

```
Dispatch only   3,035,763 msg/s  (what a naive benchmark reports)
Drained         2,981,276 msg/s  (every message handled)
```

`TellAsync` completes when a message is **accepted into a mailbox**, not when it has been handled.
Timing a loop of tells therefore measures how fast this process can fill a channel — a large,
meaningless number, and the one a naive benchmark reports.

The drained figure ends with an ask barrier per actor. An ask is ordered behind everything already
in that mailbox, so its reply arriving proves the queue ahead of it was handled. A timer or a sleep
cannot give that guarantee.

### Where the allocation goes

160–180 bytes per message sounds high for a "hot path". It is mostly the unbounded channel's own
segment storage: when senders outrun handlers, millions of envelopes are buffered, and an
`Envelope` is a struct with six fields. At 2M messages that is over 100 MB of queue.

There *is* a synchronous fast path that avoids building an async state machine when a mailbox
accepts immediately, which it always does when unbounded. It does not show up in this benchmark —
the queue-filling allocation dominates and the run-to-run variance is larger than the effect. The
saving is real in a steady-state workload, not in a queue-filling one.

## Routing and serialization

BenchmarkDotNet, `RoutingBenchmarks`:

| Operation | Members | Mean | Allocated |
| --- | --- | --- | --- |
| Hash one key | 3 | 39.1 ns | — |
| Hash one key | 12 | 38.2 ns | — |
| Place 1,024 keys on the ring | 3 | 97.8 µs (≈95 ns/key) | — |
| Place 1,024 keys on the ring | 12 | 109.4 µs (≈107 ns/key) | — |
| Serialize one message | 3 | 631 ns | 288 B |
| Serialize and deserialize | 3 | 1,230 ns | 480 B |

Three things follow.

**Placement is nearly free and barely grows with the cluster.** 95 ns at 3 members, 107 ns at 12 —
the ring is a binary search over sorted positions, so cluster size costs a logarithm. Nothing
allocates.

**Serialization costs 6× what placement does.** That is the measurement behind the design decision:
the local send path carries the message object itself, and only the wire serializes. A framework
that serialized locally would pay 630 ns and 288 bytes on every message in a single-node
deployment, for nothing.

**A remote hop is dominated by serialization and the network**, not by the runtime. If remote
throughput matters, a binary format is the lever — it is on the [roadmap](../../Plan.md).

## What these numbers do not show

This is a micro-benchmark of one machine's in-process path. Specifically, it excludes:

- **The network.** No remote hop, no TCP, no serialization on the measured path.
- **Persistence.** No store writes. A `PersistentActor` that checkpoints every message is bounded
  by its store, not by the mailbox.
- **Real handlers.** The benchmark's actor increments an integer. An actor that calls a database
  is bounded by the database, and this number is irrelevant to it.
- **Contention with anything else.** A benchmark process has the machine to itself.
- **Sustained operation.** These are short runs. There is no soak test yet.

If you are sizing a deployment, measure your own workload. The useful thing here is the *floor*:
the runtime is not what will limit you.

## Reproducing

```bash
actornet bench -n 2000000 -a 8
dotnet run -c Release --project benchmarks/ActorNet.Benchmarks
dotnet run -c Release --project benchmarks/ActorNet.Benchmarks -- --filter '*RoutingBenchmarks*'
```

Release only — BenchmarkDotNet refuses a debug build, and the CLI bench would be meaningless from
one.

## Tuning

**Mailbox capacity.** Unbounded by default, which is what makes a tell cheap. It converts a slow
consumer into unbounded memory growth, so bound it for any actor fed by an external firehose:

```csharp
options.MailboxCapacity = 10_000;   // senders wait once the actor is this far behind
```

**Idle timeout.** Shorter frees memory sooner and pays more activations; longer keeps more actors
resident. If reactivation is expensive — an event-sourced actor with a long stream and no snapshot
— lengthen it.

**Snapshots.** `SnapshotEvery` is the dial between recovery time and journal writes. A hot
event-sourced actor with no snapshot replays its whole history on every activation.

**Virtual nodes.** 128 per member keeps a 3-node ring within a few percent of even. Raising it
improves the split marginally and grows the ring; there is no reason to change it below a few dozen
members.

**Server GC is on** in `Directory.Build.props`. A node is a message pump: many small, short-lived
allocations across all cores, which is exactly the case server GC is for.

## Next

- [Architecture](02-architecture.md) — why the local path does not serialize
- [Tooling](09-tooling.md) — running the benchmarks
