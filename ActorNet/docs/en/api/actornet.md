# ActorNet

Generated - edit the `///` comments in the source, not this file.

## ActorActivationException

Thrown when activation fails and the supervisor gave up on retrying it.

### method `ActorActivationException(ActorId, Exception)`

Thrown when activation fails and the supervisor gave up on retrying it.

## ActorFailureEscalatedException

Thrown when a supervisor escalated a failure all the way to the root.

### method `ActorFailureEscalatedException(ActorId, Exception)`

Thrown when a supervisor escalated a failure all the way to the root.

## ActorId

The address of a virtual actor: an actor type name plus a user-chosen key, rendered as `Type/Key`.

The type half is what the runtime resolves against its registry to know which class to activate; the key half is opaque to the runtime and may itself contain `/`, so parsing splits on the *first* separator only. That matters for hierarchical keys such as `Device/plant-3/line-2`.

This is a struct because it is copied into every envelope and used as a dictionary key on the hot path; making it a class would put an allocation in front of every message send.

### method `ActorId(String, String)`

The address of a virtual actor: an actor type name plus a user-chosen key, rendered as `Type/Key`.

The type half is what the runtime resolves against its registry to know which class to activate; the key half is opaque to the runtime and may itself contain `/`, so parsing splits on the *first* separator only. That matters for hierarchical keys such as `Device/plant-3/line-2`.

This is a struct because it is copied into every envelope and used as a dictionary key on the hot path; making it a class would put an allocation in front of every message send.

### method `For<T0>(String)`

Builds an address for the actor type `T`.

### property `IsEmpty`

True when this is `None`.

### property `None`

An unset address. Used for "no sender" rather than a nullable, to keep envelopes flat.

### method `Parse(String)`

Parses `Type/Key`, throwing when the value has no separator.

### field `Separator`

Separator between the type half and the key half.

### method `ToString`

### method `TryParse(String, ActorId ref)`

Parses `Type/Key`, returning false instead of throwing.

## ActorInterfaceAttribute

Marks an interface as an actor's protocol, so the compiler can check calls to it.

`AskAsync<Balance>(id, new GetBalance())` is three things that have to agree and nothing that checks they do: the message, the response type, and the handler on the other side. Get any of them wrong and the failure is an `AskTimeoutException` at runtime, on the unlucky day the code path first runs.

An interface marked with this generates, at compile time:

The generated records are ordinary messages on the same allow-list as hand-written ones, so a proxy call across a node boundary is the same wire traffic it would have been by hand. What changes is that a mismatch between caller and handler is now a compile error.

### property `Alias`

Prefix for the generated message aliases. Defaults to the interface name without its leading `I`.

The alias is what travels on the wire and what a cross-language client addresses, so it is part of the protocol: renaming the interface after something is deployed would otherwise silently stop matching. Pin it here when that matters.

### property `TimeoutSeconds`

How long a generated ask waits, in seconds. Zero uses the system's default.

## ActorNetException

Base for every failure the runtime raises on purpose.

## ActorSystem

A node. Owns the actor directory, the mailbox scheduler, the supervisor, the transport and the cluster view.

**Location transparency.**`TellAsync` asks the hash ring who owns the key and then either enqueues locally or hands the message to the transport. Callers never branch on where an actor lives, and an actor that moves because the cluster changed shape keeps the same address.

**The local path does not serialize.** An in-process send puts the message object itself into the target's mailbox. Serialization exists for the wire and nowhere else, which is what makes an in-process tell cost a channel write rather than a JSON round trip.

**Deactivation has a small overlap window.** When a cell stops, it closes its mailbox and drains what it already accepted while a new send creates a fresh cell. Every message is still handled exactly once by exactly one instance, but a message accepted just before the stop can be handled after one that was sent later. Actors that care should persist through `PersistentActor`, which reloads on the new instance.

### method `ActorSystem(ActorSystemOptions, ILoggerFactory, IServiceProvider, IMessageSerializer)`

Builds a node.

- `options` — Node configuration. Validated here, so a bad setting fails at construction.
- `loggerFactory` — Where the runtime logs. Defaults to no logging.
- `services` — Used to construct actors, so they can take dependencies through their constructors. Without one, actors must have a parameterless constructor.
- `serializer` — Overrides the default JSON serializer and its type allow-list.

### method `AcquireLeaveTokenAsync(CancellationToken)`

Waits for permission to leave, so a rolling restart takes the nodes one at a time.

Returns false when the budget runs out, and the caller leaves anyway - a shutdown that blocks forever is worse than an uncoordinated one, because whatever asked this node to stop will kill it instead, and a killed node announces nothing at all.

### method `ActorOf(ActorId)`

### method `ActorOf<T0>(String)`

### method `ApplyToSiblings(ActorCell, Directive, Exception)`

Applies an all-for-one directive to the failing actor's siblings.

### method `AskAsync<T0>(ActorId, Object, Nullable<TimeSpan>, CancellationToken)`

### method `AskNodeStatusAsync(String, TimeSpan, CancellationToken)`

One peer's counters, or null when it did not answer in time.

### method `AskToLeaveAsync(Boolean, CancellationToken)`

Puts one leave request to the coordinator, or answers it here when this node is it.

### property `BoundPort`

The port the transport bound. Meaningful only after `StartAsync`.

### property `Cluster`

### method `CreateInstance(Type)`

Builds an actor instance, through the container when there is one.

Going through `ActivatorUtilities` is what lets an actor take a repository or an `HttpClient` as a constructor parameter instead of reaching for a static - which is the difference between an actor that can be unit tested and one that cannot.

### method `DeactivateAsync(ActorId, CancellationToken)`

### property `DeadLetters`

### method `DeliverInboundAsync(Envelope)`

Delivers an inbound remote message, bounding how long it may wait and telling the sender when it cannot be delivered at all.

This runs on the connection's reader loop, which is why it cannot simply await mailbox room the way a local send does: every other actor's traffic on that connection is queued behind it, so one busy actor with a bounded mailbox would stall the whole node.

It stays sequential rather than dispatching concurrently, because concurrent dispatch would break the ordering guarantee that messages from one sender to one actor arrive in order. The honest trade-off is therefore a *bounded* stall: other traffic on this connection waits at most `RemoteDeliveryTimeout` behind a full mailbox, and then the message is refused rather than waited on forever.

Every failure path answers a waiting ask. Without that, a caller on another node sees only a timeout and cannot tell a refused delivery from a slow handler.

### method `DigestLoopAsync(CancellationToken)`

Tells each successor which of this node's keys it would inherit.

Sent per successor rather than broadcast, so a peer hears only about the keys it would actually take. In a cluster of many nodes that is most of the saving: the whole directory broadcast to everybody would be the same information N times over, and useful once.

### method `DispatchAfterDeactivationAsync(Envelope, CancellationToken)`

The target deactivated between the directory lookup and the post, so try again against a fresh activation.

Removing the exact instance before retrying is what makes this terminate: the next iteration is guaranteed to build a new cell rather than spin on a corpse.

### method `DispatchLocalAsync(Envelope, CancellationToken)`

Puts an envelope into a local actor's mailbox, activating it if needed.

The retry loop is the deactivation race: a cell can start stopping between the directory lookup and the post. Removing that exact instance before retrying guarantees the next iteration builds a fresh one, so the loop terminates rather than spinning on a corpse.

### method `DispatchWithBackpressureAsync(ActorCell, Envelope, CancellationToken)`

The bounded-mailbox case: the target is behind, so the sender waits for room.

### method `DisposeAsync`

### method `DrainAsync`

Deactivates every live actor and waits for them to finish.

Deactivation runs on each actor's own loop, so they are all asked to stop and then waited on once, rather than serializing a node with thousands of actors through one shutdown at a time. Only cells that were actually built: touching a `Lazy` that has not run yet would activate an actor purely in order to deactivate it.

### method `EscalateAsync(ActorCell, Exception)`

Hands a failure up the supervision tree.

### method `FailAskAsync(Envelope, Exception)`

Answers a pending ask with the failure that happened instead of a reply.

### method `GetClusterStatusAsync(Nullable<TimeSpan>, CancellationToken)`

Asks every reachable member what its counters say, and returns them together.

The ring and the member table were already cluster-wide; the counters were not, so a console could report five members and only what one of them was doing. A node buried under work looks exactly like an idle one from three nodes away.

Peers that do not answer inside `timeout` are named in `Silent` rather than dropped. Summing four nodes and presenting it as five would be worse than showing which one is missing - and a peer going quiet is itself the thing somebody looking at this page wants to know.

### method `InheritFrom(String)`

Activates the keys a node that has gone was holding, without waiting for traffic.

Only the keys that now belong here, and only ones not already running. A key that has moved somewhere else is that node's to warm, and it heard the same digest.

### method `InspectAsync(ActorId, Nullable<TimeSpan>, CancellationToken)`

Asks an actor what it is holding, wherever in the cluster it is.

Answering a question about an actor otherwise means writing a message for the purpose, handling it and registering both - fine for a question you knew you would ask, useless for one you did not. This activates the actor if it is not already running, which is the same thing any other message would do.

### property `LocalActors`

### property `Metrics`

### property `NodeId`

### method `OnFrameAsync(WireEnvelope)`

Handles one inbound frame, whatever connection it arrived on.

### method `OnSelfDowned(String)`

Hands off actors whose keys now belong to another node.

This is the elastic half of elastic scaling. Deactivation flushes state through `OnDeactivateAsync`, and the next message re-activates the actor on its new owner from the store - so scaling out migrates roughly 1/N of the actors and nothing else moves.

### property `Options`

Options this node was built with. Mutating them after start has no effect.

### method `RecordDeadLetter(ActorId, ActorId, Object, String, DeadLetterReason, String)`

Records an undeliverable message and logs it once.

### method `RegisterActor<T0>(SupervisorStrategy)`

### method `RegisterMessage<T0>(String)`

### method `RegisterMessagesFromAssembly(Assembly)`

Registers every type in an assembly carrying `ActorMessageAttribute`.

### method `ReleaseLeaveTokenAsync(CancellationToken)`

Hands the leave token back, so the next node does not wait out the lease.

### method `ReplyAsync(Envelope, Object, CancellationToken)`

Routes a reply to whoever is waiting for it: a pending ask here, one on another node, or a sender.

### property `Serializer`

Serializer used for anything crossing a node boundary.

### method `StartAsync(CancellationToken)`

### method `StopAsync(CancellationToken)`

### method `TellAsync(ActorId, Object, ActorId, CancellationToken)`

### property `Transport`

This node's transport, for tests that need to pull the network out from under it.

Internal on purpose. An abrupt node loss is the case failure detection exists for and the one a graceful `StopAsync` cannot produce - it announces a departure, which peers act on immediately and which therefore proves nothing about detection. Closing the transport is the closest an in-process test can get to unplugging a cable.

### method `WarmSuccessorsAsync(IReadOnlyCollection<ActorId>)`

Tells whoever inherits these keys to activate them now rather than on the first message.

This is what `PreferenceList` is for: the entry after this node is the one that takes the key when this node goes. Best effort throughout - a successor that does not answer simply activates on demand later, which is what would have happened anyway, and a node on its way out is the wrong place to insist on anything.

Runs after the leave has been announced, because until then the successors do not own these keys and would forward the message straight back here.

## ActorSystemOptions

Everything that shapes a node's behaviour, in one place.

### property `AdvertisedHost`

The host peers are told to dial, when that is not the host this node binds to.

Binding and advertising answer different questions. `Host` is "which interfaces do I accept connections on", and the widest useful answer is `0.0.0.0`. This is "what should a peer type to reach me", and `0.0.0.0` is a meaningless answer to that - a peer given it will dial nothing, and eventually mark a healthy node unreachable.

Leave it null and the bind host is advertised, which is right whenever a node binds to an address peers can already route to. Set it when they differ: a container binding all interfaces but reachable by service name, or a host behind NAT.

### property `AdvertisedPort`

The port peers are told to dial, when that is not the port this node binds to.

The case this exists for is a published container port: bind 9000 inside, publish it as 19000 outside, and advertise 19000. Leave it null and the bound port is advertised - which is also what resolves `Port` being zero, since the real port is only known after the listener starts.

### property `Cluster`

Cluster membership and placement settings.

### property `DeadLetters`

Where undeliverable messages are recorded.

Bounded and dropping the oldest by default. A node that is failing to deliver usually fails a lot, and an unbounded record of that is a second outage on top of the first.

### property `DefaultAskTimeout`

Timeout applied to an ask that does not specify one.

### property `DefaultSupervisorStrategy`

Supervision policy for actors that were not registered with one of their own.

### property `EffectiveAdvertisedHost`

The host a peer should dial to reach this node.

### property `EnableNetworking`

False leaves the node completely in-process: no socket is opened.

### property `EventJournal`

Where `EventSourcedActor` appends events.

### property `Host`

Address the transport binds to. Empty disables the listener entirely (single-process mode).

### property `IdleTimeout`

How long an actor may sit without messages before the sweeper deactivates it. This is the "virtual" in virtual actor: nothing is destroyed, and the next message re-activates from persisted state.

### property `InheritanceDigestInterval`

How often a node tells its successors what it is holding.

The cost of being wrong is one cold activation, so this is deliberately slow. It trades freshness for bandwidth, and freshness is the cheaper of the two to lose.

### property `InheritanceDigestLimit`

Most keys a node tells each successor it is holding, so an unplanned loss can be warmed too.

`WarmHandoffLimit` only covers a node that leaves politely, because only that node knows what it was holding. When a node is lost without warning, the survivors inherit its keys and have no idea which of them were live - so every one of them pays a read at the moment traffic arrives, which is exactly when the cluster is already one node short.

Telling each successor in advance fixes that, at the cost of a periodic message naming keys. Off by default for that reason: unlike the warm handoff, which costs nothing until a shutdown, this is traffic a healthy cluster pays all the time. Zero turns it off.

The digest is advice, not a directory. It is a snapshot of what one node held when it last spoke, so it can name an actor that has since deactivated - warming that one costs a read and nothing else - and it can miss one activated since. It is never consulted for routing.

### method `IsUnroutable(String)`

Addresses that mean "every interface" to a listener and nothing at all to a dialler.

### property `MailboxCapacity`

Mailbox capacity per actor. Zero means unbounded; a positive value applies backpressure, so a sender awaiting a tell blocks once the target is that far behind.

Unbounded is the default because it is what makes a tell cheap, but it converts a slow consumer into unbounded memory growth. Set this on any actor fed by an external firehose.

### property `NodeId`

This node's identity. Must be unique and stable within a cluster - it is what the consistent-hash ring places keys against, so a node that comes back with a different id is a different node and takes a different slice of the keyspace.

### property `OutboundQueueCapacity`

Frames that may be queued for one peer before a send has to wait for room.

The per-peer buffer that absorbs a burst without letting an unreachable or badly-behind peer grow this node's heap without limit. Raise it for bursty traffic; lower it to feel backpressure sooner. It is a count of frames, so the memory it can hold depends on how large the messages are.

### property `Port`

Port the transport binds to. Zero picks a free port, which the system reports back after start.

### property `RemoteDeliveryTimeout`

How long an inbound remote message may wait for room in a bounded mailbox before it is refused.

A local sender waiting on a full mailbox is backpressure working: the thread being slowed down is the one producing the work. A remote sender cannot be slowed the same way, because the thread that would block is the connection's reader - and every other actor's traffic on that connection is queued behind it. One busy actor would stall the whole node.

So an inbound delivery waits, but not forever. Past this deadline the message becomes a dead letter and any waiting ask is answered with `MailboxFullException` rather than left to time out.

Unreachable unless `MailboxCapacity` is set: an unbounded mailbox always accepts.

### property `Security`

Encryption and authentication between nodes. Both off by default.

Off is the honest default for a library whose first run is on a laptop, and the reason the documentation says to keep a cluster on a trusted network until they are on.

### property `SendTimeout`

How long a send may wait for room in a peer's outbound queue before it is refused with `NodeCongestedException`.

Generous on purpose, so it fires only when a peer is genuinely stuck rather than briefly busy. Waiting indefinitely is not backpressure - it is a hang that surfaces later as an ask timeout with no indication that the peer, rather than the actor, was the problem.

### property `SnapshotStore`

Where event-sourced actors keep snapshots, so recovery does not replay from zero.

### property `StateStore`

Where `PersistentActor` keeps state. Defaults to an in-memory store, which is durable across deactivation but not across a process restart.

### property `SweepInterval`

How often the idle sweeper runs. Also the granularity of `IdleTimeout`.

### method `Validate`

Throws when the options cannot produce a working node.

### property `WarmHandoffLimit`

How many actors a departing node asks its successors to activate before it closes.

Rebalancing deactivates an actor and lets the next message reactivate it from the store, so after a planned restart the first message to every key that moved pays a read - all of them at once, at the moment traffic arrives. A leaving node knows which keys are moving and where to, and can say so.

Capped because it is one message per actor and a node can hold a great many. Beyond the cap the rest activate on demand, which is the behaviour without this at all. Zero turns it off.

### property `WireFormat`

The encoding this node writes when it opens a connection to a peer.

Reading is always both: a frame says which encoding it is in, and a connection is answered in the encoding it was addressed in. So this only decides what this node sends first, and a cluster can be rolled from one to the other a node at a time without a flag day.

The Go, Python and Node clients speak JSON only. They are unaffected - they address a node in JSON and are answered in JSON however this is set - but a client written against the binary format does not exist.

## ActorTypeNotRegisteredException

Thrown when an actor id's type half has not been registered with `RegisterActor`.

Deliberately an exception rather than a dropped message and a log line. An unregistered type is a wiring bug, and silently discarding the message only turns it into a hang somewhere else.

### method `ActorTypeNotRegisteredException(String)`

Thrown when an actor id's type half has not been registered with `RegisterActor`.

Deliberately an exception rather than a dropped message and a log line. An unregistered type is a wiring bug, and silently discarding the message only turns it into a hang somewhere else.

## AllForOneStrategy

Applies its decision to the failing actor and every one of its siblings.

### method `AllForOneStrategy(Func<Exception, Directive>)`

Applies its decision to the failing actor and every one of its siblings.

### method `Decide(Exception)`

### property `Scope`

## AskReplyTypeMismatchException

Thrown when an ask completed with a reply that is not of the expected type - almost always an actor replying with the wrong record.

### method `AskReplyTypeMismatchException(ActorId, Type, Type)`

Thrown when an ask completed with a reply that is not of the expected type - almost always an actor replying with the wrong record.

## AskTimeoutException

Thrown by an ask when no reply arrived in time.

### method `AskTimeoutException(ActorId, TimeSpan)`

Thrown by an ask when no reply arrived in time.

## DeactivationReason

Why an actor is being deactivated. Surfaced to `OnDeactivateAsync`.

### field `Idle`

No message arrived within the configured idle timeout - the normal case.

### field `Rebalanced`

The cluster moved ownership of this key to another node.

### field `Requested`

Application code asked for it, via the context or the system.

### field `Shutdown`

The node is shutting down.

### field `Supervision`

A supervisor decided to stop this actor after a failure.

## Directive

What a supervisor decides to do about a child that threw.

### field `Escalate`

Treat it as the parent's failure and let the grandparent decide.

### field `Restart`

Throw the instance away and build a fresh one. The mailbox and the address survive.

### field `Resume`

Drop the offending message and carry on with the same instance and the same state.

### field `Stop`

Deactivate the actor. The next message to that address activates a new instance.

## IActor

The contract the runtime drives. Application code normally derives from `VirtualActor` rather than implementing this directly.

Every method here is called from the actor's own mailbox loop, one at a time, so an implementation never needs a lock over its own fields. The runtime guarantees `OnActivateAsync` completes before the first `ReceiveAsync`.

### method `OnActivateAsync(IActorContext, CancellationToken)`

Called once before any message is delivered. Load persistent state here.

### method `OnDeactivateAsync(IActorContext, DeactivationReason, CancellationToken)`

Called once when the actor is going away - idle timeout, explicit stop, supervision decision, or node shutdown. Flush state here.

### method `OnRestartAsync(IActorContext, Exception, CancellationToken)`

Called after a supervisor decided to restart this actor, before the replacement is activated. The failing instance gets this; the replacement gets `OnActivateAsync`.

### method `ReceiveAsync(IActorContext, Object, CancellationToken)`

Handles one message.

## IActorContext

What an actor is handed while processing a message: its own identity, the sender's, and the handful of runtime operations that are only meaningful from inside the mailbox loop.

### property `Children`

The children spawned by this actor that are still alive.

### method `DeactivateOnIdle`

Asks the runtime to deactivate this actor once the current message is done. The next message addressed to it activates a fresh instance.

### property `Logger`

A logger scoped to this actor.

### property `Parent`

The parent in the supervision tree, or `None` for a root actor.

### method `ReplyAsync(Object, CancellationToken)`

Answers the message currently being processed. Routes to the waiting `AskAsync` caller when there is one - across the network if the ask came from another node - and otherwise to `Sender`.

**Returns:** False when the message had neither a pending ask nor a sender, so the reply went nowhere.

### property `RestartCount`

How many times this actor has been restarted by its supervisor.

### method `ScheduleTell(TimeSpan, Object, Nullable<TimeSpan>)`

Schedules a message to this actor after a delay. Survives nothing - a node restart drops pending timers, so use it for in-activation concerns, not for durable scheduling.

### property `Self`

This actor's address.

### property `Sender`

The sender of the message being processed, or `None` when it came from outside the actor system (a client, a stream, or a plain `TellAsync` with no sender).

### method `SpawnChild<T0>(String, SupervisorStrategy)`

Creates a child of this actor. The child is supervised by this actor's strategy, and is stopped when this actor stops.

### property `System`

The system this actor is running in.

### method `TellAsync(ActorId, Object, CancellationToken)`

Sends a message to another actor, stamping this actor as the sender.

### method `UnwatchAsync(ActorId, CancellationToken)`

Withdraws a watch. Harmless if there was none.

### method `WatchAsync(ActorId, CancellationToken)`

Asks to be sent a `Terminated` when `target` stops.

Only a stop that means something is reported: a supervisor giving up on the actor, or something asking it to stop. Idling out, moving during a rebalance and going down with a node are routine for a virtual actor - the address stays valid and the next message brings it back - so they are not terminations and do not notify.

The watch is held by the target's own activation, wherever that is. It does not survive the node holding it: if that node dies, nothing arrives, and a node loss is a cluster-level event rather than a per-actor one.

## IActorRef

A handle to an actor that may live in this process or on another node. Holding one says nothing about where the actor is, or whether it is currently activated.

### method `AskAsync<T0>(Object, Nullable<TimeSpan>, CancellationToken)`

Request/response. Completes when the actor calls `ReplyAsync`, and throws `AskTimeoutException` if it does not do so in time.

### property `Id`

The address this reference points at.

### method `TellAsync(Object, ActorId, CancellationToken)`

Fire-and-forget send. The returned task completes when the message is accepted into the target's mailbox (or handed to the transport), *not* when it has been processed.

## IActorSystem

The runtime: an actor directory, a mailbox scheduler, a supervisor, a transport and - when clustering is on - a membership view. One per process.

### method `ActorOf(ActorId)`

### method `ActorOf<T0>(String)`

Gets a reference to an actor. Nothing is activated by this call; activation happens on the first message, and the reference stays valid across deactivation and node moves.

### method `AskAsync<T0>(ActorId, Object, Nullable<TimeSpan>, CancellationToken)`

Convenience over `ActorOf` plus an ask.

### property `Cluster`

The cluster view. Present even in single-node mode, where it holds one member.

### method `DeactivateAsync(ActorId, CancellationToken)`

Deactivates an actor if it is currently active on this node. Idempotent.

### property `DeadLetters`

Messages that could not be delivered.

An undeliverable message used to be logged and dropped, which makes it invisible to anything but a human reading logs. Here it is a record that can be counted, inspected, and re-driven once whatever was broken is fixed.

### property `LocalActors`

Addresses of the actors currently activated on this node.

### property `Metrics`

Live counters for the CLI and the dashboard.

### property `NodeId`

This node's identity within the cluster.

### method `RegisterActor<T0>(SupervisorStrategy)`

Teaches the runtime how to activate an actor type. Required before any message addressed to that type can be delivered - the type half of an `ActorId` is resolved through this registry, never through assembly scanning.

### method `RegisterMessage<T0>(String)`

Registers a message type for cross-node serialization under an explicit alias.

### method `StartAsync(CancellationToken)`

Starts the transport, the cluster membership and the idle sweeper.

### method `StopAsync(CancellationToken)`

Deactivates every local actor and stops the transport.

### method `TellAsync(ActorId, Object, ActorId, CancellationToken)`

Convenience over `ActorOf` plus a tell.

## IInspectable

Implement to decide what `Inspect` reports for an actor.

The default reflects over the instance, which is right for most actors and wrong for one holding a password, a token or half a gigabyte of cache. An actor that implements this says what it wants seen instead - and returning null says "nothing".

### method `Inspect`

The state to report, or null to report none.

## Inspect

Asks an actor what its state currently is.

Answering questions about an actor otherwise means writing a message for the purpose, handling it, and registering both - which is fine for a question you knew you would ask and useless at three in the morning for one you did not. Every actor answers this one.

It is read on the actor's own mailbox loop, like everything else about an activation, so it never sees state half-written by a handler that is still running. The cost is that it queues behind whatever that actor is already doing: an actor that is wedged does not answer, which is itself worth knowing.

## Inspected

What an actor is holding, as JSON.

- `Actor` — The actor, in `Type/Key` form.
- `ActorType` — The runtime type of the instance, which is not always the addressed type.
- `State` — The state, as JSON. Null when the actor keeps nothing this can reach.
- `MessagesHandled` — How many messages this activation has handled.
- `ActivatedAt` — When this activation started - not when the actor first existed.

### method `Inspected(String, String, String, Int64, DateTimeOffset)`

What an actor is holding, as JSON.

- `Actor` — The actor, in `Type/Key` form.
- `ActorType` — The runtime type of the instance, which is not always the addressed type.
- `State` — The state, as JSON. Null when the actor keeps nothing this can reach.
- `MessagesHandled` — How many messages this activation has handled.
- `ActivatedAt` — When this activation started - not when the actor first existed.

### property `ActivatedAt`

When this activation started - not when the actor first existed.

### property `Actor`

The actor, in `Type/Key` form.

### property `ActorType`

The runtime type of the instance, which is not always the addressed type.

### property `MessagesHandled`

How many messages this activation has handled.

### property `State`

The state, as JSON. Null when the actor keeps nothing this can reach.

## MailboxFullException

Thrown when an actor's bounded mailbox stayed full for longer than a remote delivery may wait.

Only reachable with `MailboxCapacity` set - the default mailbox is unbounded and always accepts. A local sender blocks instead, which is the backpressure working as intended; a remote one cannot, because the thread it would block is the connection's reader and every other actor's traffic is behind it.

### method `MailboxFullException(ActorId, TimeSpan)`

Thrown when an actor's bounded mailbox stayed full for longer than a remote delivery may wait.

Only reachable with `MailboxCapacity` set - the default mailbox is unbounded and always accepts. A local sender blocks instead, which is the backpressure working as intended; a remote one cannot, because the thread it would block is the connection's reader and every other actor's traffic is behind it.

## NodeCongestedException

Thrown when a peer is reachable but so far behind that its send queue never made room.

Deliberately distinct from `NodeUnreachableException` and from `AskTimeoutException`. The three call for different responses: an unreachable node means route elsewhere, a congested one means send less or slow down, and an ask timeout means the actor itself is slow. Collapsing congestion into a timeout is what made a backed-up peer indistinguishable from a slow handler.

### method `NodeCongestedException(String, Int32, TimeSpan)`

Thrown when a peer is reachable but so far behind that its send queue never made room.

Deliberately distinct from `NodeUnreachableException` and from `AskTimeoutException`. The three call for different responses: an unreachable node means route elsewhere, a congested one means send less or slow down, and an ask timeout means the actor itself is slow. Collapsing congestion into a timeout is what made a backed-up peer indistinguishable from a slow handler.

## NodeUnreachableException

Thrown when the transport cannot reach the node that owns a key.

### method `NodeUnreachableException(String, Exception)`

Thrown when the transport cannot reach the node that owns a key.

## OneForOneStrategy

Applies its decision to the failing actor alone.

### method `OneForOneStrategy(Func<Exception, Directive>)`

Applies its decision to the failing actor alone.

### method `Decide(Exception)`

### property `Scope`

## ReceiveActor

A `VirtualActor` that dispatches on message type instead of making you write the switch. Register handlers in the constructor with `On`.

### method `OnUnhandledAsync(Object, CancellationToken)`

Called for a message with no registered handler. The default throws, so an unhandled message reaches the supervisor rather than disappearing.

### method `On<T0>(Action<T0>)`

Registers a synchronous handler for `TMessage`.

### method `On<T0>(Func<T0, CancellationToken, Task>)`

Registers an async handler for `TMessage`.

### method `ReceiveAsync(Object, CancellationToken)`

## SupervisionScope

How widely a directive is applied.

### field `AllForOne`

Every sibling under the same supervisor is affected too. Right when siblings share invariants - a set of shard workers that must be restarted as a group, for instance.

### field `OneForOne`

Only the actor that failed is affected. The Akka default, and ours.

## SupervisorStrategy

A supervisor's policy: what to do about a failure, how widely, and how many times before giving up.

Strategies are attached per actor *type* at registration, and inherited by children spawned through `SpawnChild` unless the child overrides it. Root actors - anything addressed directly rather than spawned - are supervised by the system's `DefaultSupervisorStrategy`.

The restart budget is what stops a poison message from spinning forever: exceed `MaxRestarts` within `Window` and the directive is downgraded to `Stop`, so the actor goes away instead of burning a core.

### method `BackoffFor(Int32)`

How long to wait before the `restartsInWindow`-th restart of this window.

### property `BackoffJitter`

How much to spread the wait, as a fraction either side of it. Zero is exact.

A database going down fails every actor that touches it within the same millisecond. Without jitter they would all come back in lockstep and hit it together, which is how a brief outage becomes a long one.

### method `Decide(Exception)`

Chooses what to do about `exception`.

### property `Default`

Restart on anything unexpected, but stop on a wiring bug.

The split is deliberate: a missing registration or a bad address will fail identically every time, so restarting is a busy-loop. Everything else - a transient database error, a bad payload - gets a fresh instance, which is the whole point of an actor supervisor.

### property `MaxBackoff`

A ceiling on the doubling.

Deliberately well short of `Window`: if the waits added up to more than the window, it would keep resetting and a permanently broken actor would restart forever instead of hitting `MaxRestarts` and stopping.

### property `MaxRestarts`

How many restarts are tolerated inside `Window` before stopping instead.

### property `MinBackoff`

How long the second restart in a window waits, doubling for each one after it.

The first restart is immediate, because the common failure is a one-off - a timeout, a bad payload - and making every actor pay for the rare crash loop would be the wrong trade. It is the second failure in quick succession that says the cause has not gone away, and restarting into it as fast as the mailbox allows just moves the busy-loop from the actor to whatever it depends on.

### property `ResumeOnFailure`

Logs and skips the bad message, keeping state. Suitable for idempotent read models.

### property `Scope`

How widely the directive applies.

### property `StopOnFailure`

Never restarts; a failure just stops the actor.

### property `Window`

The sliding window the restart budget is counted over.

## Terminated

Tells a watcher that the actor it was watching has stopped and will not come back on its own.

- `Actor` — The actor that stopped, in `Type/Key` form.
- `Reason` — Why it stopped.

Deliberately narrower than Akka's `Terminated`, because a virtual actor's lifecycle is not Akka's. Deactivating after an idle timeout, moving to another node during a rebalance, and going down with a node that is shutting down are all routine here: the address stays valid and the next message brings the actor back, so reporting them as terminations would be reporting bookkeeping as bereavement.

What is worth being told about is an actor that stopped for a reason: a supervisor gave up on it, or something asked it to stop. Those are the two reasons that arrive here.

### method `Terminated(String, DeactivationReason)`

Tells a watcher that the actor it was watching has stopped and will not come back on its own.

- `Actor` — The actor that stopped, in `Type/Key` form.
- `Reason` — Why it stopped.

Deliberately narrower than Akka's `Terminated`, because a virtual actor's lifecycle is not Akka's. Deactivating after an idle timeout, moving to another node during a rebalance, and going down with a node that is shutting down are all routine here: the address stays valid and the next message brings the actor back, so reporting them as terminations would be reporting bookkeeping as bereavement.

What is worth being told about is an actor that stopped for a reason: a supervisor gave up on it, or something asked it to stop. Those are the two reasons that arrive here.

### property `Actor`

The actor that stopped, in `Type/Key` form.

### property `Id`

The actor that stopped.

### property `Reason`

Why it stopped.

## UnknownMessageTypeException

Thrown when a message type arrives over the wire without a registered alias.

### method `UnknownMessageTypeException(String)`

Thrown when a message type arrives over the wire without a registered alias.

## Unwatch

Withdraws a `Watch`.

- `Watcher` — The actor that no longer wants to be told.

### method `Unwatch(String)`

Withdraws a `Watch`.

- `Watcher` — The actor that no longer wants to be told.

### property `Watcher`

The actor that no longer wants to be told.

## UrgentAttribute

Marks a message type that may overtake a backlog in an actor's mailbox.

A mailbox is ordered, and that ordering is a guarantee: messages from one sender to one actor are handled in the order they were sent. Marking a type urgent gives that guarantee up for that type against every other type - which is the entire point of it, and the reason it is declared on the message rather than passed at the send. A type either overtakes or it does not, and both ends can see which by reading the type. A per-send flag would mean the same message sometimes overtook and sometimes did not, and nothing at the receiving end could tell you why.

Ordering *within* a lane still holds: two urgent messages from one sender arrive in order, as do two ordinary ones. Only the relationship between the two lanes is given up.

It works the same way for a message that arrived over the wire, because the receiving node resolves the alias to this type before posting it. Nothing in the protocol carries priority.

## UrgentMessages

Answers whether a message type is urgent, once per type.

Reflection on every send would cost more than the feature saves. The answer cannot change for a given type, so it is asked once and cached for the life of the process.

## VirtualActor

The base class application actors derive from.

"Virtual" means the same thing it does in Orleans: an actor always exists conceptually, is activated by the runtime on its first message, and is deactivated again once it has been idle. Nothing creates or destroys one explicitly, and a reference stays valid across both.

The `IActor` members are implemented explicitly and forwarded to the protected overloads below, which do not take a context - it is already on `Context`. That keeps the runtime's contract out of the shape a user writes against.

One message is processed at a time, so fields need no synchronization. The corollary is that `Context` describes the message currently being handled: capturing it into a background task and using it later reads someone else's sender.

### property `Context`

Identity, sender and runtime operations for the message being handled.

### method `OnActivateAsync(CancellationToken)`

Runs once before the first message. Load state here.

### method `OnDeactivateAsync(DeactivationReason, CancellationToken)`

Runs once on the way out. Flush state here.

### method `OnRestartAsync(Exception, CancellationToken)`

Runs on the failing instance after a supervisor chose to restart it.

### method `ReceiveAsync(Object, CancellationToken)`

Handles one message.

### method `ReceiveCoreAsync(Object, CancellationToken)`

The runtime's entry point into a message, wrapping `ReceiveAsync`.

Base classes that need to run something around every message - persistence checkpointing, tracing - override this rather than the abstract `ReceiveAsync`, which belongs to the application actor at the bottom of the hierarchy.

## Warm

Asks the receiving node to activate an actor now, rather than on the first message.

Sent by a node that is shutting down, to whoever inherits its keys. Rebalancing works by deactivating an actor and letting the next message reactivate it from the store, so the first message to every key that moved pays a read. On a rolling restart that is every actor the node held, all at once, at the moment traffic arrives.

The receiving cell handles this itself and the actor never sees it. Activation is the entire point of the message; there is nothing else to do with it.

## Watch

Asks an actor to report when it stops. Sent by `WatchAsync`.

- `Watcher` — The watching actor, in `Type/Key` form.

An ordinary message rather than a new frame kind, so it travels the path every other message takes - routed to whichever node owns the target, serialized through the same allow-list. The receiving cell handles it itself; the actor never sees it.

### method `Watch(String)`

Asks an actor to report when it stops. Sent by `WatchAsync`.

- `Watcher` — The watching actor, in `Type/Key` form.

An ordinary message rather than a new frame kind, so it travels the path every other message takes - routed to whichever node owns the target, serialized through the same allow-list. The receiving cell handles it itself; the actor never sees it.

### property `Watcher`

The watching actor, in `Type/Key` form.

---

[Back to the index](README.md)
