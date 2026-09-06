# ActorNet — product roadmap

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

Where this is going and why. For what is actually built right now, see
[Progress.md](Progress.md) — the development tracking checklist.

---

## The thesis

Orleans makes distributed systems approachable by hiding the lifecycle: you address an actor and it
is there. Akka.NET makes them survivable by exposing it: you decide what happens when one fails.
Most teams end up wanting both and picking one.

ActorNet's bet is that the two are not actually in tension. A virtual actor can have a supervision
strategy. A grain can be event-sourced. The lifecycle can be automatic *and* the failure policy
explicit, because they answer different questions — "when does this exist?" and "what happens when
it breaks?"

## Where it stands

Version 0.1 is a working framework, not a prototype: virtual actors, supervision, clustering with
consistent hashing, two persistence models, reactive streams, a console, desktop samples, and four
client SDKs. It is not yet a framework anyone should run a bank on, and the roadmap below is mostly
about closing that gap.

## 0.2 — durability and operability — **done**

The things that stood between this and a production pilot: database persistence, dead letters,
OpenTelemetry, and flow control that a remote sender can see. See
[Progress.md](Progress.md) for what each of those turned into.

## 0.3 — clustering that survives a bad day

The membership layer is deliberately simple, and simple has limits worth being explicit about.
Adaptive failure detection is the first of these to land: suspicion is now measured against each
peer's own heartbeat history rather than one deadline chosen for the worst link.

| Theme | Why it matters |
| --- | --- |
| Replica placement | `PreferenceList` exists and nothing uses it. Standby replicas would make a node loss invisible. |
| Rolling upgrade | A node now flushes before it announces its leave, so a rolling restart is safe. What is left is the warm half: handing the actors to the next owner rather than making it load them on the first message. |

## 0.4 — ecosystem — **done**

Three things that make the framework easier to live with rather than more capable: an
`[ActorInterface]` the compiler checks calls against, `ActorNet.AspNetCore` for the deployment a
node is most likely to be in, and a binary envelope for the traffic between nodes. See
[Progress.md](Progress.md) for what each turned into, and what each stopped short of.

## 0.5 — the shape of the remaining gaps

Nothing here is a feature so much as an admission. Each is something already built that works in
one place and not another. The console now collects counters from every peer, all four clients fail
over between nodes, and a departing node hands its actors to their successors; what is left is
below, with the reasoning rather than only the intent.

| Theme | Why it matters |
| --- | --- |
| A binary payload | The envelope is binary; the message body is still JSON, and measurement says it is the larger half of a real frame. |
| Coordinated rolling restarts | A node hands its actors over cleanly, but nothing stops two nodes leaving at once. |

### A binary payload, measured

Worth writing down before anyone starts. Payload share of a binary frame, measured on this machine:

| Frame | Payload | Envelope |
| --- | --- | --- |
| A tell with one small field | 40% | 60% |
| An ask with an empty request | 14% | 86% |
| A reply carrying a statement | 69% | 31% |
| A telemetry reading | 59% | 41% |
| An order-saga step | 61% | 39% |

So for anything but the smallest message the payload is the larger half, and a binary body is worth
having. What it costs is the part to weigh: `IMessageSerializer` returns a `JsonElement`, and
`WireEnvelope.Payload` is one, so a binary body means changing the serializer contract and the
envelope that four language clients read. The generator that already emits message records could
emit binary readers and writers for them, which is the tractable half; the wire change is not
something to do quickly.

### Replica placement — not planned as originally written

`PreferenceList` now has a user: a departing node uses it to find each key's successor and hand the
actors over. What the entry originally meant - standby replicas holding live state so a node loss is
invisible - is a different thing, and it contradicts the bet the rest of the design is built on.
State lives in the store; an actor is resident, not authoritative. A standby holding live state
would need replication and conflict resolution between two copies of an actor, which is
[actor migration with in-flight state](#deliberately-not-planned) under another name.

What is achievable is narrower and is written down as its own line: eagerly activating inherited
keys after an *unplanned* loss the way a planned one already does. It needs the survivors to know
which keys the dead node held, and nothing tells them that today.

## Deliberately not planned

- **Exactly-once delivery.** At-most-once with an idempotent handler is the honest primitive.
  Promising more would mean a transaction log on every send.
- **Actor migration with in-flight state.** Deactivate-and-reactivate through the store is
  simpler, and it is what makes an actor's state durable rather than merely resident.
- **A DSL for supervision trees.** Registration plus `SpawnChild` covers it. A DSL would be
  something new to learn for no new capability.

---
