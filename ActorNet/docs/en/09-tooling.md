# Tooling

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[Bahasa Indonesia](../id/09-perkakas.md) · [Docs index](README.md)*

Three surfaces over the same runtime: a terminal CLI, a web console, and desktop samples.

## The CLI

```bash
dotnet tool install -g ActorNet.Cli   # the "actornet" command
```

| Command | |
| --- | --- |
| `run` | Start a node and keep it running |
| `monitor` | Start a node with a live dashboard in the terminal |
| `demo <scenario>` | Run a worked scenario |
| `cluster` | Join a cluster and show membership and key placement |
| `bench` | Measure throughput |
| `scenarios` | List the scenarios and what each demonstrates |

Shared options: `--node-id`, `--host`, `-p|--port`, `--seed` (repeatable), `--cluster`, `--data`,
`--idle-timeout`, `-v|--verbose`.

`--data <dir>` swaps the in-memory stores for file-backed ones, which is what makes "stop it, start
it again, the balance is still there" demonstrable rather than merely claimed.

Set `ACTORNET_DEBUG=1` for full stack traces. By default a failure prints one line — a stack trace
is the right thing for a framework bug and the wrong thing for "port 9000 is already in use", which
is most of what goes wrong.

### Demos

```bash
actornet demo banking      # event-sourced accounts; 200 concurrent deposits, no locks
actornet demo telemetry    # a reactive stream into one actor per device
actornet demo ordering     # a saga that compensates when payment fails
actornet demo lifecycle    # activate, deactivate, reactivate from the journal
actornet demo              # a menu
```

Each prints what it is about to do, does it, and then shows the number that proves it worked.

### Monitor

```bash
actornet monitor --load --refresh 250 --top 20
```

Redraws a fixed layout in place rather than scrolling — a scrolling monitor is unreadable well
below what this runtime does. `--load` generates synthetic traffic so there is something to watch.

A clustered node also gets a member table beside the busiest actors, which is this node's own view
of the membership rather than a shared truth:

![The terminal monitor watching a three-node cluster](../images/monitor-3nodes.png)

```bash
actornet monitor --host 0.0.0.0 --advertised-host 10.0.1.5 --port 9000 --cluster --load
```

On a standalone node the table is omitted — it would say one thing the header already says.

### Bench

```bash
actornet bench -n 2000000 -a 8
```

Reports two numbers on purpose:

```
Dispatch only   3,035,763 msg/s  (what a naive benchmark reports)
Drained         2,981,276 msg/s  (every message handled)
```

A tell completes when the message is *accepted* into a mailbox, so timing a loop of tells measures
how fast the process can fill a channel. The drained figure is bounded by an ask barrier per actor,
which is ordered behind everything already queued — its reply proves the queue drained. See
[Performance](10-performance.md).

## The web console

```bash
dotnet run --project src/ActorNet.Dashboard
```

It is itself a node, so the numbers are the runtime's own counters rather than a scrape.

```bash
dotnet run --project src/ActorNet.Dashboard -- \
  --ActorNet:NodeId=console-1 \
  --ActorNet:Port=9100 \
  --ActorNet:Seeds:0=127.0.0.1:9000
```

Set `ActorNet:GenerateLoad=false` to watch a real workload instead of a synthetic one.

### Overview

![Console overview](../images/console-overview.png)

Throughput, messages handled, in flight, active actors, failures, restarts — and the busiest
actors, since on a node with thousands the interesting ones are the ones doing work.

**In flight** counts messages accepted for handling *on this node*. A message forwarded to the node
that owns its key is not in flight here. Getting that wrong made the console read 3,524 in flight
against 26 active actors, which is what caught the bug.

### Actors

![Console actors](../images/console-actors.png)

Every activation on this node, filterable, with **Inspect** and **Deactivate** buttons.

**Inspect** asks the actor what it is holding and expands a panel under its row. The answer is
produced on the actor's own mailbox loop, so it is never state a handler is halfway through
writing — and an actor that is wedged does not answer at all, which is itself the useful answer.

An actor with a `State` — anything deriving from `PersistentActor` or `EventSourcedActor` — reports
that. One without reports the fields its own class declares, not the framework's: a mailbox and a
handler table are not what you asked about. An actor that holds something it would rather not show
implements `IInspectable` and says what it wants seen instead:

```csharp
public sealed class SessionActor : ReceiveActor, IInspectable
{
    private readonly string _token;
    private int _requests;

    public object? Inspect() => new { requests = _requests, tokenLength = _token.Length };
}
```

Returning `null` from `Inspect()` shows nothing at all.

Deactivating is not destructive: it runs the deactivation hook, where a persistent actor flushes,
and removes the actor from this node. The address stays valid and the next message activates a
fresh instance from the store — the same thing the idle sweeper does.

### Cluster

![Console cluster](../images/console-cluster.png)

The member table, and the ring drawn as it actually is: one arc per span of hash space, coloured by
its owning node. The stripes are the virtual nodes, and their interleaving is why adding a member
moves roughly 1/N of the keyspace.

Type an address into the probe and a marker lands on the arc that owns it — the same computation
the runtime does on every send.

### HTTP API

```bash
curl localhost:5170/api/metrics
curl localhost:5170/api/cluster
curl localhost:5170/api/deadletters
```

Read-only, so the same numbers are available to a scrape or a script without screen-scraping.

## The desktop samples

```bash
dotnet run --project samples/ActorNet.Samples.Avalonia
```

Four scenarios over one shared node, started once and kept for the life of the window. Each states
the property of the actor model it demonstrates, and then proves it.

**Banking** — 800 concurrent deposits from 16 tasks into one account. The sample states the
expected balance *before* running, which is the only way an assertion like that means anything.

![Banking](../images/samples-banking.png)

**Telemetry** — a live stream into one actor per device, with an over-temperature device to
exercise the alarm path. "Deactivate all devices" and keep streaming: they come back with their
counts intact.

**Ordering** — a saga across inventory and payment. Set the credit limit below an order's total and
the saga fails at payment *after* stock was reserved; watch the held count go back up. "Try to
oversell" fires more single-unit orders than there is stock and the number never goes negative.

**Supervision** — four actors of the same class, registered with different strategies, given the
same exception.

![Supervision](../images/samples-supervision.png)

## Benchmarks

```bash
dotnet run -c Release --project benchmarks/ActorNet.Benchmarks
dotnet run -c Release --project benchmarks/ActorNet.Benchmarks -- --filter '*RoutingBenchmarks*'
```

BenchmarkDotNet, with memory diagnostics. `MessagingBenchmarks` covers the message path;
`RoutingBenchmarks` covers hashing, placement and serialization.

## Dead letters

An undeliverable message is recorded rather than logged and dropped. Logging alone makes it
invisible to everything except a human reading logs.

```csharp
foreach (var letter in system.DeadLetters.Recent(20))
    Console.WriteLine($"{letter.Target} {letter.MessageType}: {letter.Reason} - {letter.Detail}");

system.DeadLetters.LetterRecorded += letter => alerting.Raise(letter);
```

| Reason | |
| --- | --- |
| `UnregisteredActorType` | The address named a type this node never registered |
| `UndeliverableToActor` | The actor kept deactivating between lookup and delivery |
| `NodeUnreachable` | The node owning the key could not be reached |
| `UnknownMessageType` | A frame named an alias the allow-list refuses |
| `UnroutableFrame` | A malformed address, or a frame with no payload |
| `Shutdown` | The node was stopping |

The message object is kept where it was materialized, so a letter can be re-driven once whatever was
broken is fixed. It is deliberately **absent** for a frame refused before deserialization -
materializing a payload the allow-list just refused would undo the refusal.

The buffer is bounded and drops the oldest; `Count` is still the exact lifetime total. A node that is
failing to deliver is usually failing a lot, and an unbounded record of that is a second outage on
top of the first. Swap it with `options.DeadLetters`.

## Inside an ASP.NET Core application

`ActorNet.AspNetCore` is the integration for the deployment this is most likely to be in: a node
living inside a web application, where an orchestrator decides whether to send it traffic.

```csharp
builder.Services.AddActorNet(actors => { /* ... */ });
builder.Services.AddHealthChecks().AddActorNetCheck();

var app = builder.Build();

app.MapHealthChecks("/ready", new() { Predicate = r => r.Tags.Contains("ready") });
app.MapActorNetDiagnostics().RequireAuthorization();
app.MapGet("/orders/{id}", …).RequireActorNetReady();
```

**The health check reports what the node can see, not that the process is up.** A node that has
lost the cluster and is answering for keys it no longer owns looks perfectly alive to a plain
liveness probe. Three outcomes:

| | When |
| --- | --- |
| Healthy | Every member it knows about is reachable |
| Degraded | Some peer is unreachable - it is still serving its own keys correctly |
| Unhealthy | This node is off the ring: it lost a partition, or it is leaving |

Unreachable peers are degraded rather than unhealthy on purpose. Unreachable means "missed a few
beats, still on the ring", and restarting a node over that turns a blip into a rebalance.

**`MapActorNetDiagnostics` exposes read-only endpoints** — `/actornet/cluster` for the membership
and each member's share of the keyspace, `/actornet/metrics` for the counters, and
`/actornet/actors/{type}/{key}` for one actor's state. It is what the console shows, for a
deployment that has no console. Nothing is authorized by default and it exposes node addresses and
actor keys, so the returned group takes `RequireAuthorization()` like any other.

The actor endpoint is the console's **Inspect** over HTTP, routed by the ring like any other
message, so it answers for an actor on any node. It activates the actor if it is not already
running — the same thing any message would do. An unregistered actor type is a `404`; an actor that
does not answer within five seconds is a `504`, which usually means it is busy rather than gone,
because an inspection queues behind whatever it is handling.

**`RequireActorNetReady()` answers 503 with a `Retry-After`** while the node is off the ring, for
the window between it deciding it is done and the load balancer noticing.

## Exporting to OpenTelemetry

The console reads the runtime's own counters, which is fine for one node and useless for anything
that aggregates. The standard .NET primitives are exposed for that:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(ActorNetDiagnostics.ActivitySourceName))
    .WithMetrics(m => m.AddMeter(ActorNetDiagnostics.MeterName));
```

ActorNet takes no dependency on OpenTelemetry to provide these - they are an `ActivitySource` and a
`Meter`, so any collector picks them up.

| Instrument | |
| --- | --- |
| `actornet.messages.processed` | Messages handled, by actor type |
| `actornet.messages.failed` | Handlers that threw, by actor type and exception |
| `actornet.actors.activated` / `.deactivated` / `.restarted` | Lifecycle, deactivations tagged with the reason |
| `actornet.deadletters` | Undelivered, tagged with the reason |
| `actornet.message.duration` | Time in the handler |
| `actornet.message.queue_time` | Time waiting in a mailbox |

Of the two histograms, **queue time is the one to watch**. Handler time says how expensive the work
is; queue time says whether the node is keeping up with it.

Each handled message also produces a span, `"<ActorType> receive"`, of kind `Consumer` - so a trace
viewer draws it as the receiving half of a send. A handler that throws marks its span as an error and
attaches the exception.

**Spans follow a message across a node boundary.** Every frame that leaves a node carries the W3C
`traceparent` and `tracestate` of whatever span was current when it was sent, and the receiving node
starts its span under that parent. An ask answered three machines away is one trace with the hops
nested inside it, rather than three traces that cannot be told apart afterwards.

Replies and failures are stamped the same way, which is the half that shows how long the caller
actually waited rather than how long the handler took.

The fields are the standard ones, `tp` and `tw` on the wire, so the Go, Python and Node clients can
fill them in from their own tracing without knowing anything about this runtime. A message that
arrives without them - a timer tick, a client that does not trace - simply starts a trace of its
own.

None of this costs anything when nothing is listening: `StartActivity` returns null without a
listener, and an instrument with no collector does not record. That is what makes tracing affordable
on the per-message path.

## Next

- [Performance](10-performance.md)
- [Troubleshooting](11-troubleshooting.md)
