# actornet-client — Node.js client

Node.js client for [ActorNet](https://github.com/DotNetVibeCoderz/Vibe_Library/tree/main/ActorNet),
a hybrid .NET actor framework: Orleans-style virtual actors with Akka-style supervision,
clustering, persistence and event sourcing.

This package talks to an ActorNet node over the node's own wire protocol — a 4-byte big-endian
length followed by JSON. There is no HTTP gateway to deploy or keep in sync with the runtime.

No dependencies: it is `node:net` and `node:crypto`.

```bash
npm install actornet-client
```

## Usage

```javascript
const { ActorNetClient } = require('actornet-client');

const client = new ActorNetClient({ host: '127.0.0.1', port: 9000, clientId: 'web-1' });
await client.connect();

// Fire and forget. Resolves once the node accepts the message, not once it is handled.
await client.tell('BankAccountActor/alice', 'bank.deposit', { Amount: 500, Reference: 'opening' });

// Request/response.
const { alias, payload } = await client.ask('BankAccountActor/alice', 'bank.get-statement', { MaxEntries: 5 });
console.log(alias, payload.Balance);

client.close();
```

## Addressing

Actors are addressed by string — `'BankAccountActor/alice'` is an actor type, a `/`, then a key.

Messages are addressed by **alias**, not by .NET type name. The node resolves an incoming alias
through an explicit allow-list, so an unregistered alias is refused rather than constructed. That
refusal is the security property — a transport that resolves whatever type name arrives lets a peer
choose what the process builds — and it is also what makes a Node process able to address the same
actors as a C# one.

On the .NET side an alias is declared on the message:

```csharp
[ActorMessage(Alias = "bank.deposit")]
public sealed record Deposit(decimal Amount, string Reference = "");
```

Payload keys are the .NET property names (`Amount`, `Reference`), matched case-insensitively.

## What a client is, and is not

A client is **not** a cluster member. It connects to one node, and that node forwards to whichever
node owns the target actor — so any node is a valid entry point.

What it does not get is a membership view of its own. It cannot tell you where an actor lives, and
if the node it is connected to goes down it must reconnect elsewhere rather than failing over on its
own. Reconnect and cluster-aware routing are on the roadmap.

Because a client has no address the cluster can dial, the node answers on the connection the client
opened. That is why the client keeps one long-lived socket and keeps reading it even when only
sending: an `ask` reply has nowhere else to arrive.

## API

| | |
| --- | --- |
| `new ActorNetClient({ host, port, clientId, askTimeoutMs })` | `clientId` must be unique among a node's clients; one is generated if omitted. |
| `connect()` | Opens the socket. `tell` and `ask` call it for you. |
| `tell(target, alias, payload)` | Fire and forget. |
| `ask(target, alias, payload, timeoutMs)` | Resolves to `{ alias, payload }`. |
| `close()` | Closes the socket and rejects anything still waiting. |

## Errors

| | |
| --- | --- |
| `AskTimeoutError` | No reply arrived in time. Usually the handler never calls `ReplyAsync`, or the actor's mailbox is long. |
| `ActorNetError` | The actor failed while handling the request, or the connection dropped. The node sends the failure back rather than leaving you on a timeout. |

```javascript
const { ActorNetError, AskTimeoutError } = require('actornet-client');

try {
  await client.ask('PaymentActor/cust-1', 'order.charge', { Amount: 50 });
} catch (err) {
  if (err instanceof AskTimeoutError) { /* … */ }
  else if (err instanceof ActorNetError) { console.error(`the actor failed: ${err.message}`); }
  else throw err;
}
```

## Running the example

Start a node with the demo domain registered:

```bash
dotnet run --project src/ActorNet.Cli -- run --port 9000
```

Then:

```bash
node examples/banking.js
```

It opens an event-sourced account, deposits, withdraws, reads a statement, and shows an overdraft
being refused — a refusal comes back as a normal reply, not an error, because the actor decided
rather than failed.

`ACTORNET_HOST` and `ACTORNET_PORT` override the defaults.

## Documentation

- [Client SDKs and the wire protocol](https://github.com/DotNetVibeCoderz/Vibe_Library/blob/main/ActorNet/docs/en/08-clients.md)
- [ActorNet documentation](https://github.com/DotNetVibeCoderz/Vibe_Library/tree/main/ActorNet/docs/en)
- [Bahasa Indonesia](https://github.com/DotNetVibeCoderz/Vibe_Library/tree/main/ActorNet/docs/id)

## License

MIT.

---

Dibuat oleh **Gravicode Studios**, dipimpin oleh **Kang Fadhil**.
