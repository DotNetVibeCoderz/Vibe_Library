# ActorNet client SDKs

Four clients, one protocol. Each speaks the node's own wire format directly - a 4-byte big-endian
payload length followed by that many bytes of JSON - so there is no HTTP gateway to deploy or keep
in sync with the runtime.

| Language | Path | Verified against a live node |
| --- | --- | --- |
| C# | `src/ActorNet.Core/Client/ActorNetClient.cs` | yes, in `tests/ActorNet.Tests/ClientTests.cs` |
| Node.js | `clients/nodejs/` | yes, `examples/banking.js` |
| Python | `clients/python/` | yes, `examples/telemetry.py` |
| Go | `clients/go/` | **not run** - no Go toolchain on the machine this was written on |

## What a client can and cannot do

A client is **not** a cluster member. It connects to one node, and that node forwards to whichever
node owns the target actor, so any node is a valid entry point. What a client does not get is a
membership view of its own: it cannot tell you where an actor lives, and if the node it is
connected to goes down, it must reconnect somewhere else rather than failing over on its own.

Because a client has no address the cluster can dial, the node answers on the connection the client
opened. That is why every client keeps one long-lived socket and keeps reading it even when it is
only sending: an `ask` reply has nowhere else to arrive.

## Addressing

Actors are addressed by string: `"BankAccountActor/alice"` - the actor type name, a `/`, then the
key. Messages are addressed by **alias**, not by .NET type name:

```csharp
[ActorMessage(Alias = "bank.deposit")]
public sealed record Deposit(decimal Amount, string Reference = "");
```

The node resolves an incoming alias through an explicit allow-list. A type that is not registered
is refused, which is what stops a peer from naming a type for the node to construct - the shape of
attack that deserialization gadgets are built on. It is also what makes cross-language clients work
at all: `bank.deposit` means the same thing in Go and in C#, and neither side needs the other's
type names.

Payload field names are the .NET property names (`Amount`, `Reference`), matched
case-insensitively.

## Running the examples

Start a node with the demo domain registered:

```bash
dotnet run --project src/ActorNet.Cli -- run --port 9000
```

Then, in another terminal:

```bash
node clients/nodejs/examples/banking.js
python clients/python/examples/telemetry.py
cd clients/go && go run ./examples/ordering
```

Each example sets `ACTORNET_HOST` / `ACTORNET_PORT` (Go uses `ACTORNET_ADDR`) if you moved the
node.

---

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
