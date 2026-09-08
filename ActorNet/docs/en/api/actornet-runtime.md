# ActorNet.Runtime

Generated - edit the `///` comments in the source, not this file.

## ActorCell

One activated actor: its instance, its mailbox, the loop that drains it, and everything the supervisor needs to restart or stop it.

The loop is the single thread of control for an actor. Activation runs on it, every message runs on it, supervision decisions about *this* actor run on it, and deactivation runs on it. Nothing outside reaches in and mutates the instance - other components ask by posting a `SystemCommand`.

That is what closes the activation race a naive implementation has: the loop awaits `OnActivateAsync` before it reads the first message, so a message can queue during activation but can never be handled by an actor whose state has not finished loading.

### method `Abort`

Forces the loop to unwind without draining. Used only when a graceful stop timed out.

### method `AnswerInspectAsync(Envelope, CancellationToken)`

Answers `Inspect` with whatever this actor is holding.

Runs on the mailbox loop like every other message, so it never reads state a handler is halfway through writing. An actor that is wedged does not answer, which is a true thing to learn about it.

### method `ApplySupervisionAsync(Exception, CancellationToken)`

Runs the supervising strategy's decision about a failure in this actor.

### property `Idle`

How long since this actor last handled anything - what the idle sweeper reads.

### field `InspectionJson`

Lenient on purpose: this serializes types nobody wrote for a serializer.

### method `NotifyWatchersAsync(DeactivationReason)`

Tells anyone watching that this actor has stopped, when the reason is one worth reporting.

`Idle`, `Rebalanced` and `Shutdown` are routine for a virtual actor: the address stays valid and the next message brings it back somewhere. Reporting those as terminations would train every watcher to ignore them, which would make the two that matter useless too.

Sent after the cell is unregistered, so a watcher that responds by messaging the address gets a fresh activation rather than a queue on a mailbox that is already closed.

### method `PostAsync(Envelope, CancellationToken)`

Enqueues a message. False means this cell is on its way out and the caller should look the address up again, which will activate a fresh one.

### method `PostSystem(SystemCommand)`

Posts a lifecycle instruction, bypassing the state check that `PostAsync` does. A stop has to be deliverable to a cell that is already stopping.

### method `RequestStop(DeactivationReason)`

Closes the mailbox and asks the loop to wind down after draining. Returns false when the cell was already stopping, so the caller knows not to count the deactivation twice.

### method `RestartAsync(Exception, CancellationToken)`

Replaces the actor instance in place. The address, mailbox and children survive.

### method `RestartBudgetAllows`

Asks the supervising strategy's restart budget whether another restart is allowed.

### method `Start`

Starts the loop. Called once, by the directory, before the cell is published.

### method `StateOf(IActor)`

The `State` of a persistent or event-sourced actor, or the fields the actor declares.

Those two base classes keep the interesting part behind a protected `State`, and serializing the actor around it would report the plumbing rather than the data.

An actor without one keeps everything in private fields, which no serializer will touch on its own - so they are read here by reflection. Only the fields the actor's own class declares: walking into the framework's base classes would report a mailbox and a handler table to somebody asking what an account is holding.

### property `Stopped`

Completes once the loop has exited and deactivation has run.

### method `TryPostFast(Envelope ref)`

Enqueues without ever suspending. Null means "could not answer synchronously" - the mailbox is bounded and full - and the caller should fall back to `PostAsync`.

Do not expect this to show up in the throughput benchmark. When senders outrun handlers, the run's allocation is dominated by the channel's own segment storage for the millions of buffered envelopes - measured at 160-180 B/msg either way, with more run-to-run variance than the state machine costs. The saving is real but it is visible in a steady-state workload, not in a queue-filling one.

### method `TryStopIfIdle(TimeSpan)`

Idle-stops the cell, but only if it is genuinely idle: a non-empty mailbox means work arrived between the sweeper's check and this call.

### field `_watchers`

Who asked to be told when this actor stops, in `Type/Key` form.

Only ever touched from the mailbox loop, like everything else about an activation, so it needs no lock. Watches are per-activation on purpose: an actor that idles out and comes back is a new activation with nothing to report, which is the same reason idling is not a termination.

## ActorRef

A handle to an actor. There is deliberately only one implementation for local and remote actors: routing is decided per message by the hash ring, so a reference cannot be "the local one" - the same address can be local now and remote after the next rebalance.

### method `ActorRef(ActorSystem, ActorId)`

A handle to an actor. There is deliberately only one implementation for local and remote actors: routing is decided per message by the hash ring, so a reference cannot be "the local one" - the same address can be local now and remote after the next rebalance.

### method `AskAsync<T0>(Object, Nullable<TimeSpan>, CancellationToken)`

### property `Id`

### method `TellAsync(Object, ActorId, CancellationToken)`

### method `ToString`

## ActorRegistration

What the runtime knows about an actor type.

### method `ActorRegistration(String, Type, SupervisorStrategy)`

What the runtime knows about an actor type.

## ChannelMailbox

The default mailbox: a `Channel`, single-reader (the actor's loop), multi-writer.

Unbounded is the default because it makes a tell complete synchronously, which is what keeps fan-out cheap. A bounded mailbox trades that for backpressure - the sender's `PostAsync` stops completing synchronously once the actor falls behind, which pushes the slowdown back to whoever is producing rather than into this process's heap.

A second lane appears the first time a message marked `UrgentAttribute` is posted, and not before: an actor that never sees one is a single channel, exactly as it was. That is why the lane is created on demand rather than configured - there is nothing to switch on, and no way to mark a message urgent and then find the mailbox was not listening.

### method `ChannelMailbox(Int32)`

Creates a mailbox. `capacity` of zero or less means unbounded.

### method `Complete`

### property `Count`

### method `LaneFor(Envelope ref)`

The lane a message belongs in, creating the urgent one the first time it is needed.

### method `PostAsync(Envelope, CancellationToken)`

### method `TryPost(Envelope ref)`

### method `TryRead(Envelope ref)`

### field `UrgentBurst`

An ordinary message is taken after this many urgent ones, even with urgent messages queued.

Strict priority is simpler to describe, and under it a sender producing urgent messages faster than the actor handles them starves the ordinary lane for as long as it keeps going - a queue that never drains and never errors. Eight is high enough that an urgent message still overtakes any backlog worth the name; being finite is the part that matters.

### method `WaitToReadAsync(CancellationToken)`

## DeadLetter

One message that never reached an actor.

- `Target` — Where it was headed. May be empty when the address itself was unusable.
- `Sender` — Who sent it, when that was known.
- `Message` — The message, when it had been materialized. Null for a frame refused before deserialization - which is deliberate: materializing a refused payload would undo the allow-list that refused it.
- `MessageType` — The message's type or wire alias, which is known even when the body is not.
- `Reason` — Why delivery failed.
- `Detail` — The exception message or diagnostic behind the reason.
- `At` — When it was recorded.

### method `DeadLetter(ActorId, ActorId, Object, String, DeadLetterReason, String, DateTimeOffset)`

One message that never reached an actor.

- `Target` — Where it was headed. May be empty when the address itself was unusable.
- `Sender` — Who sent it, when that was known.
- `Message` — The message, when it had been materialized. Null for a frame refused before deserialization - which is deliberate: materializing a refused payload would undo the allow-list that refused it.
- `MessageType` — The message's type or wire alias, which is known even when the body is not.
- `Reason` — Why delivery failed.
- `Detail` — The exception message or diagnostic behind the reason.
- `At` — When it was recorded.

### property `At`

When it was recorded.

### property `Detail`

The exception message or diagnostic behind the reason.

### property `Message`

The message, when it had been materialized. Null for a frame refused before deserialization - which is deliberate: materializing a refused payload would undo the allow-list that refused it.

### property `MessageType`

The message's type or wire alias, which is known even when the body is not.

### property `Reason`

Why delivery failed.

### property `Sender`

Who sent it, when that was known.

### property `Target`

Where it was headed. May be empty when the address itself was unusable.

## DeadLetterQueue

The default queue: a bounded ring in memory.

### method `DeadLetterQueue(Int32)`

The default queue: a bounded ring in memory.

### method `Clear`

### property `Count`

### event `LetterRecorded`

### method `Recent(Int32)`

### method `Record(DeadLetter)`

## DeadLetterReason

Why a message could not be delivered.

### field `MailboxFull`

A bounded mailbox stayed full for longer than an inbound remote delivery may wait.

Only reachable with a bounded mailbox, and only on the inbound remote path - a local sender waits instead, which is the backpressure doing its job.

### field `NodeUnreachable`

The node that owns the key could not be reached.

### field `Shutdown`

The node was stopping when the message arrived.

### field `UndeliverableToActor`

The actor kept deactivating between the directory lookup and the post.

### field `UnknownMessageType`

A frame arrived naming a message alias this node does not allow.

### field `UnregisteredActorType`

The actor id's type half was never registered on this node.

### field `UnroutableFrame`

A frame arrived that could not be routed - a malformed address, or no payload.

## Envelope

One message in flight, plus the routing metadata the runtime needs to deliver a reply.

A struct on purpose: this is written into a channel for every single send, and the local path deliberately carries the *materialized*`Message` rather than a serialized payload. Only the network transport serializes.

### property `CorrelationId`

Set when a caller is blocked in AskAsync waiting for a reply.

### method `Create(ActorId, Object, ActorId, String, String, String, String)`

Creates an envelope stamped with the current timestamp.

### property `EnqueuedTimestamp`

Stopwatch timestamp at enqueue time, so the mailbox loop can report queue latency without a second clock read on the sending side.

### property `Message`

The message itself, already materialized.

### property `ReplyToNode`

The node that owns the pending ask. Null means "this node"; a value means the reply has to go back over the wire.

### property `Sender`

Who sent it, or `None`.

### property `Target`

Where the message is going.

### property `TraceParent`

W3C `traceparent` of the span on the sending node, for a message that came off the wire. Null for a local send, where `Current` is still the caller's and needs no help.

### property `TraceState`

W3C `tracestate` that arrived with the frame.

## IDeadLetterQueue

Where undeliverable messages go.

Before this, an undeliverable message was logged and dropped, which makes it invisible to anything but a human reading logs. A dead letter is a record an operator can look at, count, and - because the message object is kept where it was materialized - re-drive once whatever was broken is fixed.

The buffer is bounded and drops the oldest. A node that is failing to deliver is usually failing a lot, and an unbounded record of that is a second outage on top of the first.

### method `Clear`

Forgets everything retained. The lifetime count is not reset.

### property `Count`

How many messages have been recorded since the node started, including dropped ones.

### event `LetterRecorded`

Raised for each letter, on the thread that recorded it.

Subscribers should be quick and must not throw - this runs on the sending path, and a slow handler here slows down the thing that is already going wrong.

### method `Recent(Int32)`

The retained letters, newest first.

### method `Record(DeadLetter)`

Records an undeliverable message.

## IMailbox

A single actor's message queue.

### method `Complete`

Closes the mailbox to new messages. Anything already queued is still delivered, which is what makes a graceful stop graceful.

### property `Count`

Messages accepted but not yet handed to the actor.

### method `PostAsync(Envelope, CancellationToken)`

Enqueues a message, waiting when the mailbox is bounded and full. Returns false once the mailbox has been closed, which is how a sender learns the actor is going away and that it should look the address up again.

### method `TryPost(Envelope ref)`

Non-blocking enqueue. False when the mailbox is closed or full.

### method `TryRead(Envelope ref)`

Takes the next message if one is queued.

### method `WaitToReadAsync(CancellationToken)`

Completes when a message is available; false once the mailbox is closed and drained.

## RestartCommand

Rebuild the actor instance, keeping the address and the queued messages.

### method `RestartCommand(Exception)`

Rebuild the actor instance, keeping the address and the queued messages.

## StopCommand

Deactivate the actor once the messages ahead of this one are done.

### method `StopCommand(DeactivationReason)`

Deactivate the actor once the messages ahead of this one are done.

## SystemCommand

A runtime instruction to an actor, delivered through its own mailbox rather than applied from outside.

Going through the mailbox is what makes lifecycle changes safe: a restart or a stop is then ordered against the messages around it and runs on the actor's own loop, so it cannot land in the middle of a half-finished `ReceiveAsync`. The cost is that a stop waits behind whatever is already queued, which is exactly what "graceful" means here.

---

[Back to the index](README.md)
