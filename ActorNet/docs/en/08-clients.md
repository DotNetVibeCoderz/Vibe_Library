# Clients

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[Bahasa Indonesia](../id/08-klien.md) · [Docs index](README.md)*

Four SDKs — C#, Node.js, Python, Go — all speaking the node's own protocol. There is no HTTP
gateway to deploy or keep in sync with the runtime.

## What a client is, and is not

A client is **not** a cluster member. It connects to one node, and that node forwards to whichever
node owns the target actor, so any node is a valid entry point.

What a client does not get is a membership view of its own. It cannot tell you where an actor
lives, and it does not know which node owns a key - it sends to whichever node it is connected to
and lets the ring do the rest.

Given more than one endpoint, a client reconnects to another node when the one it is using goes
away. All four do this the same way, and each is checked in CI against a dead address placed ahead
of a live one.

Because a client has no address the cluster can dial, **the node answers on the connection the
client opened**. That is why every client keeps one long-lived socket and keeps reading it even
when it is only sending: an `ask` reply has nowhere else to arrive.

## Addressing

Actors are addressed by string: `"BankAccountActor/alice"` — the type name, a `/`, then the key.

Messages are addressed by **alias**, not by .NET type name:

```csharp
[ActorMessage(Alias = "bank.deposit")]
public sealed record Deposit(decimal Amount, string Reference = "");
```

The node resolves an incoming alias through an explicit allow-list. An unregistered alias is
refused with `UnknownMessageTypeException`.

That refusal is the security property: a transport that resolves whatever type name arrives lets a
peer choose which type the process constructs, which is what deserialization gadget chains are
built on. It is also what makes cross-language clients possible — `bank.deposit` means the same
thing in Go and in C#, and neither side needs the other's type names.

Payload field names are the .NET property names (`Amount`, `Reference`), matched
case-insensitively.

## Routing in a cluster

Any node is a valid entry point. A frame that arrives from something which is not a cluster member -
a client - for a key that node does not own is **forwarded once** to the node that does, and the
reply is carried back through the node the client asked. So every client is correct in a cluster
whether it knows the ring or not.

That forwarding is deliberately not symmetric. A frame from a *member* is always handled where it
arrives, because a peer routed with its own view and bouncing its message onward is what risks a
loop between two nodes disagreeing during a rebalance. Only a sender absent from the member table is
treated as unrouted.

What it costs is a hop. The C# client can avoid it:

```csharp
var client = new ActorNetClient(["10.0.0.1:9000", "10.0.0.2:9000"]) { ClusterAware = true };
```

It asks a node for the member table, builds the same ring the cluster uses — the same hash, the same
virtual-node count, so it reaches the same answer — and opens a connection per node it actually
addresses. The view is refreshed every `RoutesRefreshAfter` (30 seconds by default) and whenever a
connection to a node named by it fails.

Routing degrades rather than fails. A client that cannot get a view, or whose view names a node that
will not answer, sends to the node it is already connected to and is forwarded from there.

The one case that does surface an error is an ask whose answer was on a connection that died — the
node it was talking to went away while the question was in flight. That fails **promptly** rather
than waiting out the timeout, and the client drops the connection and throws its view away, so the
caller's next attempt routes afresh. It is the same contract a client with no routing at all has
when an endpoint disappears.

**The Node.js, Python and Go clients do not route.** They are correct without it and pay the hop.

One thing to know about the hop: the node that forwarded an ask holds the correlation until the
answer arrives, in memory. If that node restarts while a question is in flight, the client sees a
timeout rather than an answer from elsewhere.

## The wire protocol

A frame is four bytes of big-endian length, then that many bytes of UTF-8 JSON. Fields are short
because they are on every hop:

| Field | Meaning |
| --- | --- |
| `k` | Kind: 1 message, 2 ask request, 3 ask reply, 4 ask failure |
| `t` | Target actor, `"Type/Key"` |
| `s` | Sending actor, when there is one |
| `a` | Message alias |
| `p` | Payload, as JSON |
| `c` | Correlation id, for an ask |
| `r` | Node the reply must go to |
| `f` | Node or client that sent this frame |
| `e` | Error text, on an ask failure |

Frames above 32 MiB are refused on both sides, so a bad length cannot make either end allocate
wildly.

## C#

```csharp
await using var client = new ActorNetClient("127.0.0.1", 9000, clientId: "reporting-service");
client.RegisterMessagesFromAssembly(typeof(Deposit).Assembly);

await client.TellAsync(ActorId.Parse("BankAccountActor/alice"), new Deposit(500m));
var statement = await client.AskAsync<Statement>(ActorId.Parse("BankAccountActor/alice"), new GetStatement());
```

Give it more than one node and it survives losing the one it dialled:

```csharp
await using var client = new ActorNetClient(["10.0.1.5:9000", "10.0.1.6:9000", "10.0.1.7:9000"]);
```

It does not matter which node a client reaches - one that does not own the target key forwards by
the ring - so every node is an equally correct entrance, and a client bound to exactly one of them
is a strange thing for a client of a cluster to be.

Endpoints are tried in rotation from the last one that worked, so an ordinary reconnect goes back
where it was and only a node that is really gone costs a move. `ConnectedTo` says which one is in
use. Rotating on every connect would be churn rather than balance: there is nothing to gain by
moving and a connection to re-establish by moving.

**Anything in flight when a connection drops still fails**, with an `ActorNetException` naming the
endpoint and carrying the socket error as its cause. Delivery is at-most-once, and re-sending a
request whose reply was lost would quietly make it at-least-once. The caller knows whether its
operation is safe to repeat; the client does not - so the call after the failure is the one that
finds another node.

## Node.js

```javascript
const { ActorNetClient } = require('./actornet');

const client = new ActorNetClient({ endpoints: ['10.0.1.5:9000', '10.0.1.6:9000'], clientId: 'web-1' });
await client.connect();

await client.tell('BankAccountActor/alice', 'bank.deposit', { Amount: 500, Reference: 'opening' });

const { alias, payload } = await client.ask('BankAccountActor/alice', 'bank.get-statement', { MaxEntries: 5 });
console.log(alias, payload.Balance);

client.close();
```

## Python

```python
from actornet import ActorNetClient

async with ActorNetClient(endpoints=["10.0.1.5:9000", "10.0.1.6:9000"], client_id="ingest-1") as client:
    await client.tell("DeviceActor/sensor-001", "iot.reading",
                      {"DeviceId": "sensor-001", "Celsius": 21.5, "At": now})

    reply = await client.ask("DeviceActor/sensor-001", "iot.get-status", {})
    print(reply.payload["Average"])
```

## Go

```go
client := actornet.NewCluster([]string{"10.0.1.5:9000", "10.0.1.6:9000"}, actornet.WithClientID("worker-1"))
defer client.Close()

if err := client.Tell(ctx, "InventoryActor/widget", "order.restock",
    map[string]any{"Sku": "widget", "Quantity": 10}); err != nil {
    return err
}

reply, err := client.Ask(ctx, "InventoryActor/widget", "order.get-stock", map[string]any{})
if err != nil {
    return err
}

var stock stockLevel
if err := reply.Into(&stock); err != nil {
    return err
}
```

## What every client does the same way

**One persistent connection.** Dialling per message costs a handshake every time, exhausts the
ephemeral port range under load, and makes `ask` impossible because the reply has nowhere to
arrive.

**Serialized writes.** Several callers may be sending at once; interleaved writes would produce
frames nobody sent.

**Length-prefixed reads.** TCP is a byte stream, so a chunk is not a frame. Two replies can arrive
coalesced and one large reply arrives in pieces. Every client buffers until a whole frame is
present.

**Correlation-id matching.** Replies arrive interleaved on one socket. A client that matched them
by arrival order would hand callers each other's answers — there is a test for exactly this, with
40 asks in flight.

## Verification status

| Client | Status |
| --- | --- |
| C# | Verified in the test suite — tell, ask, 40 concurrent asks, failure, timeout, allow-list refusal |
| Node.js | Verified against a live node; run in CI |
| Python | Verified against a live node; run in CI |
| Go | **Not run** on the machine it was written on — no Go toolchain there. CI compiles it, vets it, and drives it against a real node |

Being straight about that last row matters more than the row being empty.

## Running the examples

```bash
dotnet run --project src/ActorNet.Cli -- run --port 9000
```

Then:

```bash
node clients/nodejs/examples/banking.js
python clients/python/examples/telemetry.py
cd clients/go && go run ./examples/ordering
```

Each honours `ACTORNET_HOST` / `ACTORNET_PORT` (Go uses `ACTORNET_ADDR`).

## Not built yet

- Cluster-aware routing, so a client sends straight to the node that owns the key

See the [roadmap](../../Plan.md).

## Next

- [Architecture](02-architecture.md) — where the protocol sits
- [Clustering](06-clustering.md) — why any node is a valid entry point
