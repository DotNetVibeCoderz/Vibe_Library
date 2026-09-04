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

## 0.2 — durability and operability

The things that stand between this and a production pilot.

| Theme | Why it matters |
| --- | --- |
| Backpressure that reaches the sender | A bounded mailbox blocks the local sender today, but a remote sender only sees a full transport queue. |
| Structured diagnostics (`ActivitySource`, `Meter`) | The console reads the runtime's own counters; nothing exports to OpenTelemetry yet. |
| Dead letters | An undeliverable message is logged and dropped. It should be observable and re-drivable. |
| Ask over a bounded transport queue | A slow peer currently makes an ask time out with no way to distinguish it from a slow actor. |

## 0.3 — clustering that survives a bad day

The membership layer is deliberately simple, and simple has limits worth being explicit about.

| Theme | Why it matters |
| --- | --- |
| Phi-accrual failure detection | A fixed deadline calls a GC pause a failure. An adaptive detector does not. |
| Gossip fanout limits | Every node gossips to every node: fine at tens, quadratic past that. |
| Split-brain resolution | Two halves of a partitioned cluster each believe they own the whole ring. Today nothing arbitrates. |
| Replica placement | `PreferenceList` exists and nothing uses it. Standby replicas would make a node loss invisible. |
| Rolling upgrade | Handing off a node's actors before it stops, rather than deactivating them and waiting for traffic. |

## 0.4 — ecosystem

| Theme | Why it matters |
| --- | --- |
| Source-generated actor proxies | `AskAsync<Balance>(id, new GetBalance())` could be `account.GetBalanceAsync()`, checked at compile time. |
| ASP.NET Core integration package | Endpoint filters and health checks that know about actors. |
| Kubernetes discovery | Seeds are static strings. A headless service should be enough. |
| A binary wire format | JSON is the right default and the wrong choice for a hot inter-node path. |

## Deliberately not planned

- **Exactly-once delivery.** At-most-once with an idempotent handler is the honest primitive.
  Promising more would mean a transaction log on every send.
- **Actor migration with in-flight state.** Deactivate-and-reactivate through the store is
  simpler, and it is what makes an actor's state durable rather than merely resident.
- **A DSL for supervision trees.** Registration plus `SpawnChild` covers it. A DSL would be
  something new to learn for no new capability.

---
