# Architecture

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[Bahasa Indonesia](../id/02-arsitektur.md) · [Docs index](README.md)*

## The journey of one message

```
system.TellAsync(ActorId("BankAccountActor", "alice"), new Deposit(100m))
│
├─ Cluster.IsLocal(id)?  ──── hash the address, find its owner on the ring
│
├─ yes ─→ GetOrCreateCell ─→ ActorCell.TryPostFast ─→ Channel<Envelope>
│                                                          │
│                                    the actor's own loop ─┘
│                                    ├─ OnActivateAsync (first message only)
│                                    ├─ ReceiveAsync
│                                    └─ supervision, if it threw
│
└─ no ──→ serialize ─→ WireEnvelope ─→ TcpTransport ─→ the owning node
                                                            │
                                       …which lands in the same local path above
```

Five things are worth understanding about that picture.

## 1. Addresses are the whole routing story

An `ActorId` is `Type/Key` — `BankAccountActor/alice`. The type half is resolved against the
runtime's registry to know which class to build. The key half is opaque, and may itself contain
`/`, so parsing splits on the **first** separator only: `DeviceActor/plant-3/line-2` is one device.

Placement hashes the whole address. Two nodes handed the same member list compute the same ring and
agree on every key without asking each other. That agreement is the entire distributed-coordination
budget of this framework — there is no directory service, no lock manager, and no consensus round.

The hash is FNV-1a followed by the MurmurHash3 finalizer, over UTF-8 bytes. It is emphatically not
`string.GetHashCode()`, which is randomised per process: two nodes would compute different rings
from the same members and disagree about who owns what, and that bug only appears in a real
cluster.

## 2. The local path does not serialize

An in-process send puts the **message object itself** into the target's mailbox. Serialization
exists for the wire and nowhere else.

This is the single biggest departure from a naive implementation, and it is worth the asymmetry: a
local send costs a channel write (measured against ~95 ns of placement), while serializing the same
message costs ~630 ns and 288 bytes. A framework that serialized locally would pay that on every
message in a single-node deployment for no benefit at all.

## 3. One actor, one thread of control

Every activation is an `ActorCell`: an instance, a mailbox, and one loop draining it.

```
ActorCell.RunAsync
  await OnActivateAsync          ← finishes before any message is read
  while (mailbox has messages)
      StopCommand?    → deactivate
      RestartCommand? → rebuild the instance in place
      otherwise       → ReceiveAsync, and supervise anything it throws
```

Activation runs on that loop, every message runs on it, supervision decisions about that actor run
on it, and deactivation runs on it. Nothing outside reaches in and mutates the instance — other
components *ask*, by posting a command into the same mailbox.

That ordering is what closes a race a naive implementation has. If activation is fired off with
`Task.Run` while the mailbox loop is already reading, a message can be handled by an actor whose
state has not finished loading. Here, a message can queue during activation but can never be
handled before it.

It is also why your actor needs no locks. Fields are touched by exactly one thread at a time, and
the framework never calls into your actor from anywhere else.

## 4. Lifecycle changes travel through the mailbox

A restart or a stop is not applied from outside. It is posted as a `SystemCommand` into the actor's
own queue, so it is *ordered* against the messages around it and runs on the actor's own loop.

The cost is that a stop waits behind whatever is already queued — which is exactly what "graceful"
means. The benefit is that a lifecycle change can never land in the middle of a half-finished
`ReceiveAsync`.

## 5. The wire is length-prefixed and allow-listed

Every frame is four bytes of big-endian length, then that many bytes of JSON.

TCP is a byte stream, not a message stream. Treating "whatever one read returned" as one message is
the classic bug: it works on localhost with small payloads and corrupts the moment two sends
coalesce into one segment or one message spans two. The length prefix is what makes a frame a
frame, and there is a size cap so a hostile peer cannot announce a 4 GB frame.

Inbound payloads are resolved through an **explicit type allow-list**, keyed by a registered alias:

```csharp
[ActorMessage(Alias = "bank.deposit")]
public sealed record Deposit(decimal Amount, string Reference = "");
```

Never `Type.GetType(nameOnTheWire)`. A transport that resolves whatever name arrives lets a peer
choose which type this process constructs, which is what deserialization gadget chains are built
on. It is also what makes the Go, Python and Node clients work: `bank.deposit` means the same thing
everywhere, and neither side needs the other's type names.

## Components

```
src/ActorNet.Core/
  ActorId.cs                 the address
  ActorSystem.cs             the node: directory, routing, asks, shutdown
  VirtualActor.cs            what you derive from
  ReceiveActor.cs            type-dispatched handlers
  ActorSystemOptions.cs      everything configurable, validated at construction

  Abstractions/              IActor, IActorRef, IActorContext, IActorSystem, exceptions
  Runtime/                   ActorCell (lifecycle + supervision), Mailbox, Envelope, ActorRef
  Supervision/               SupervisorStrategy, directives, scope, restart budget
  Persistence/               PersistentActor, EventSourcedActor, in-memory and file stores
  Serialization/             the type allow-list and the JSON serializer
  Network/                   FrameCodec (framing), TcpTransport (connections)
  Cluster/                   membership gossip, failure detection, HashRing
  Streams/                   ActorStream operators and the actor sink
  Metrics/                   counters and snapshots
  Hosting/                   AddActorNet and the hosted service
  Client/                    ActorNetClient, for processes that are not nodes
```

## Deliberate limits

**Deactivation has a small overlap window.** When a cell stops it closes its mailbox and drains
what it already accepted, while a new send builds a fresh cell. Every message is still handled
exactly once by exactly one instance, but a message accepted just before the stop can be handled
after one sent later. Actors that care should persist, so the new instance reloads.

**Rebalancing deactivates rather than migrates.** An actor whose key moves to another node is
flushed and reactivated there from the store. In-memory-only state does not survive that, by
design: migrating live state would mean a distributed handover protocol, and the store already
solves the problem.

**Delivery is at-most-once.** A message accepted into a mailbox is handled unless the process dies.
There is no ack, no retry, and no redelivery. At-most-once plus an idempotent handler is the honest
primitive; anything more would need a transaction log on every send.

## Next

- [Actors and lifecycle](03-actors.md)
- [Clustering](06-clustering.md) — the ring and membership in detail
- [Performance](10-performance.md) — the measurements behind the claims above
