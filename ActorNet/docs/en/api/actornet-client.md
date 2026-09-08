# ActorNet.Client

Generated - edit the `///` comments in the source, not this file.

## ActorNetClient

Talks to an ActorNet node from a process that is not itself a node.

One persistent connection, not one per message. Dialling per message costs a handshake every time and exhausts the ephemeral port range under load - and it makes ask impossible, because the reply has nowhere to arrive.

The node addresses this client by the id in `ClientId`: it is not a cluster member, so the node has no address to dial back and instead answers on this connection. That is why asks work here at all, and why the client must keep reading even when it is only telling.

A client connects to *one* node. If the actor it addresses lives elsewhere in the cluster, that node forwards it - so any node is a valid entry point, but this client does not itself track membership.

### method `ActorNetClient(IEnumerable<String>, String, IMessageSerializer)`

Connects to whichever of these nodes answers.

- `endpoints` — Nodes as `host:port`. Any of them will do.

A client bound to one node goes down with it, which is a strange property for a client of a cluster. It does not matter which node it reaches: a node that does not own the target key forwards by the ring, so every node is an equally correct entrance.

Anything in flight when a connection drops still fails. Delivery is at-most-once, and re-sending a request whose reply was lost would quietly turn it into at-least-once - the caller knows whether its operation is safe to repeat and this does not.

### method `ActorNetClient(String, Int32, String, IMessageSerializer)`

Connects to one node.

### method `AskAsync<T0>(ActorId, Object, Nullable<TimeSpan>, CancellationToken)`

Sends a message and waits for the actor's reply.

### method `AskClusterViewAsync(CancellationToken)`

Asks the node this client is connected to for the member table.

### property `ClientId`

How this client identifies itself to the node. Must be unique among its clients.

### property `ClusterAware`

Whether to work out which node owns a key and send straight to it.

Off by default. With it off a client sends everything to the node it is connected to, which forwards - correct, and one extra hop for every message whose actor lives elsewhere. With it on the client asks a node for the member table, builds the same ring the cluster uses, and opens a connection per node it actually addresses.

It is an optimisation and never a requirement: every node accepts a message for any actor. A client whose view is stale sends to the wrong node and the message still arrives, which is what makes it safe to route by a view that is seconds behind.

### method `ConnectAsync(CancellationToken)`

Opens a connection to whichever endpoint answers. Called automatically on first use.

Tried in rotation from the last one that worked, so an ordinary reconnect goes back where it was and only a node that is actually gone costs a move.

### property `ConnectedNodes`

The nodes this client currently holds a connection to.

### property `ConnectedTo`

The endpoint currently in use, in `host:port` form, or null when not connected.

### property `DefaultTimeout`

Default timeout for `AskAsync`.

### method `DisposeAsync`

### property `Endpoints`

The endpoints this client may use, in the order they were given.

### property `IsConnected`

True while the connection is up.

### method `OwnerLinkAsync(ActorId, CancellationToken)`

The connection to whoever owns this key, or null to use the first connection.

### method `RegisterMessage<T0>(String)`

Registers a message type under an alias. Both ends must agree on the alias.

### method `RegisterMessagesFromAssembly(Assembly)`

Registers every attributed message type in an assembly.

### method `RoutesAsync(CancellationToken)`

The member table, asked for again once it is old enough.

### property `RoutesRefreshAfter`

How long a member table is used before it is asked for again.

### method `SendAsync(WireEnvelope, ActorId, CancellationToken)`

Sends a frame, to the owner of `target` when routing is on.

### method `TellAsync(ActorId, Object, CancellationToken)`

Sends a message and returns once the node has accepted it.

### property `Types`

The type allow-list. Register every message and reply type before using them.

### field `_alive`

Whether this client believes the connection is usable.

`Connected` reports the result of the last I/O rather than the state of the socket, so it stays true after the peer has gone until something tries to use it. Trusting it meant a client whose node had stopped kept writing into a dead socket and waited out its own ask timeout instead of reconnecting. The reader knows first, so the reader decides.

### field `_cursor`

Which endpoint to try first. Sticky: it only moves when one fails.

Rotating on every connect would spread clients evenly and reconnect somewhere new after every blip, which is churn rather than balance - any node forwards by the ring anyway, so there is nothing to gain by moving and a connection to re-establish by moving.

---

[Back to the index](README.md)
