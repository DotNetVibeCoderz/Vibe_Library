# Actors and lifecycle

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[Bahasa Indonesia](../id/03-actor.md) · [Docs index](README.md)*

## The three base classes

| Base | State lives | Pick it when |
| --- | --- | --- |
| `VirtualActor` | In memory only | You want the raw `ReceiveAsync` switch |
| `ReceiveActor` | In memory only | You want handlers registered by message type |
| `PersistentActor<TState>` | One record per actor | The current value is what matters |
| `EventSourcedActor<TState>` | An append-only journal | The history matters |

`ReceiveActor` is the usual starting point:

```csharp
public sealed class CounterActor : ReceiveActor
{
    private int _total;

    public CounterActor()
    {
        On<Add>(m => _total += m.By);
        On<GetTotal>(async (_, ct) => await Context.ReplyAsync(new Total(_total), ct));
    }
}
```

An unhandled message throws by default, so it reaches your supervisor rather than disappearing.
Override `OnUnhandledAsync` if ignoring it is genuinely what you want.

## The lifecycle

```
      ┌──────────────┐
      │  not active  │  ← the address exists; nothing is running
      └──────┬───────┘
             │ first message arrives
             ▼
     OnActivateAsync            ← awaited before any message is handled
             │
             ▼
      ┌──────────────┐
      │    active    │  ← ReceiveAsync, one message at a time
      └──────┬───────┘
             │ idle timeout · DeactivateAsync · supervision stop
             │ cluster rebalance · node shutdown
             ▼
    OnDeactivateAsync           ← flush state here
             │
             ▼
      ┌──────────────┐
      │  not active  │  ← the next message starts the cycle again
      └──────────────┘
```

Nothing creates or destroys an actor. `ActorOf` returns a reference to an address, and that
reference stays valid across every deactivation and every node move.

## Activation

```csharp
protected override async Task OnActivateAsync(CancellationToken ct)
{
    _rates = await _rateService.LoadAsync(Context.Self.Key, ct);
}
```

Guaranteed to complete before the first `ReceiveAsync`. Messages that arrive during activation
queue up; they cannot be handled by a half-initialised actor.

If activation throws, the actor is **not** started and the messages waiting for it are lost. That
is deliberate: an actor that cannot load its state will fail identically every time, so restarting
it is a busy-loop. The failure is logged as `ActorActivationException`.

## Deactivation

```csharp
protected override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
{
    if (reason == DeactivationReason.Supervision) return;   // do not flush after a failure
    await _repository.SaveAsync(_state, ct);
}
```

`DeactivationReason` tells you why:

| Reason | Meaning |
| --- | --- |
| `Idle` | No message within the idle timeout. The normal case. |
| `Requested` | `system.DeactivateAsync(id)` or `Context.DeactivateOnIdle()`. |
| `Supervision` | A supervisor stopped it after a failure. |
| `Rebalanced` | The cluster moved this key to another node. |
| `Shutdown` | The node is stopping. |

`PersistentActor` already skips the flush on `Supervision` — writing back state that a failed
message may have left half-updated is how a transient bug becomes a permanent one.

Deactivation gets 30 seconds. Past that the loop is aborted and whatever it was doing is lost.

## Idle deactivation

```csharp
options.IdleTimeout = TimeSpan.FromMinutes(5);
options.SweepInterval = TimeSpan.FromSeconds(15);
```

A sweeper runs on `SweepInterval` and stops any actor idle past `IdleTimeout` **with an empty
mailbox** — the second condition matters, because work can arrive between the check and the stop.

This is what makes the model tractable at scale: a million registered devices are a million
addresses, but memory only holds the ones currently reporting.

An actor can also retire itself:

```csharp
On<Finish>(_ => Context.DeactivateOnIdle());   // stops after this message completes
```

## The context

`Context` describes the message **currently being handled**. Capturing it into a background task
and reading it later gives you someone else's sender.

| Member | |
| --- | --- |
| `Self` | This actor's address |
| `Sender` | Who sent the current message, or `ActorId.None` |
| `Parent` | The supervising actor, or `ActorId.None` at the root |
| `System` | The node |
| `Logger` | A logger scoped to this actor |
| `RestartCount` | How many times a supervisor has rebuilt this actor |
| `Children` | Addresses spawned by this actor that are still alive |
| `TellAsync` | Send, stamping this actor as the sender |
| `ReplyAsync` | Answer the current message |
| `SpawnChild<T>` | Create a supervised child |
| `ScheduleTell` | Send to self after a delay |
| `DeactivateOnIdle` | Retire after the current message |

## Replying

```csharp
await Context.ReplyAsync(new Total(_total), ct);
```

`ReplyAsync` routes to whoever is waiting, in this order:

1. A pending `AskAsync`, on this node or another one — matched by correlation id, not by which
   socket the request arrived on.
2. Otherwise `Sender`, as an ordinary message.
3. Otherwise nowhere, and it returns `false`.

That third case is worth checking if you expect a conversation. A `TellAsync` with no sender leaves
a reply nothing to route to.

## Children and supervision trees

```csharp
On<OpenSession>(m =>
{
    var child = Context.SpawnChild<SessionActor>(m.SessionId);
    _sessions.Add(child.Id);
});
```

A child inherits its parent's supervision strategy unless it is given its own, and stops when the
parent stops — children first, so a parent's `OnDeactivateAsync` can still reach them.

Note that a child is a normal addressable actor. `SpawnChild<SessionActor>("abc")` creates
`SessionActor/abc`, which anyone can address directly. The parent relationship governs supervision
and shutdown, not visibility.

## Scheduling

```csharp
_timer = Context.ScheduleTell(TimeSpan.FromSeconds(30), new Sweep(), repeatEvery: TimeSpan.FromSeconds(30));
```

Returns an `IDisposable`; dispose it to cancel. Timers do **not** survive deactivation or a node
restart — this is for in-activation concerns, not durable scheduling.

## Calling an actor through an interface

`AskAsync<Balance>(id, new GetBalance())` is three things that have to agree — the request type,
the response type and the handler on the other side — and nothing that checks they do. Get one
wrong and the failure is an `AskTimeoutException` on the unlucky day that code path first runs.

Declare the protocol once and the compiler checks it:

```csharp
[ActorInterface]
public interface IBankAccount
{
    Task DepositAsync(decimal amount, string reference);
    Task<decimal> GetBalanceAsync(CancellationToken cancellationToken = default);
}
```

A source generator turns that into a request record per method, a reply record per method that
returns something, a proxy, and a base class for the actor:

```csharp
public sealed class BankAccountActor : BankAccountActorBase   // generated
{
    private decimal _balance;

    public override Task DepositAsync(decimal amount, string reference)
    {
        _balance += amount;
        return Task.CompletedTask;
    }

    public override Task<decimal> GetBalanceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_balance);
}

BankAccountProtocol.Register<BankAccountActor>(system);        // generated

var account = BankAccountProxy.Of<BankAccountActor>(system, "acct-001");
await account.DepositAsync(250m, "opening");
var balance = await account.GetBalanceAsync();
```

Nothing exotic is generated. The records carry the same `[ActorMessage]` alias a hand-written
message would and the proxy calls the same `TellAsync` and `AskAsync`, so a proxy call across a
node boundary is the traffic the manual version produced. What changes is that a mismatch between
caller and handler is a compile error.

Four rules the generator enforces, each with a message saying why:

| | |
| --- | --- |
| Methods return `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>` | A call is a message and a reply, so it has to be awaitable |
| No `ref`, `out` or `in` parameters | Parameters become fields of a message that may cross a process |
| No generic methods | The message type would have to be registered before it exists |
| No properties or events | A property reads as synchronous access to state on another thread |

**A method returning `Task` is a tell**, and completes when the message is accepted — not when the
handler is done. A method returning `Task<T>` is an ask and waits for the reply. That is the
existing distinction, made visible in the signature.

**The implementing type is named at the call site** because an actor's address is its type name and
its key, and an interface does not know which actor implements it. The generic constraint is what
stops a proxy being pointed at an actor that does not speak the protocol.

**A node that only calls the actor uses `Register(system)`**, without the type argument: it needs
the messages on its allow-list but must not register an actor type it will never host, or the ring
would believe it can own those keys.

## Watching another actor

```csharp
await Context.WatchAsync(ActorId.For<PaymentActor>(orderId));

// ... later, in the same actor
On<Terminated>(t => Logger.LogWarning("{Actor} stopped: {Reason}", t.Actor, t.Reason));
```

`Terminated` arrives when the watched actor stops **for a reason**: a supervisor gave up on it, or
something asked it to stop. `Context.UnwatchAsync` withdraws the interest.

**This is narrower than Akka's `Terminated`, on purpose.** A virtual actor's lifecycle is not an
Akka actor's. Three of the five deactivation reasons are routine bookkeeping here:

| Reason | Notifies | Why |
| --- | --- | --- |
| `Supervision` | **yes** | A supervisor stopped it after a failure |
| `Requested` | **yes** | Something asked it to stop |
| `Idle` | no | It timed out; the next message brings it back, same address |
| `Rebalanced` | no | It moved to another node; the address is unchanged |
| `Shutdown` | no | Its node is stopping; it reactivates elsewhere |

Reporting the bottom three would train every watcher to ignore the notification, which would make
the two that mean something useless as well.

Two things worth knowing:

**The watch lives on the target's activation**, wherever in the cluster that is. `WatchAsync` is an
ordinary message routed by the ring, so watching an actor on another node works the same as watching
one next door, and the notice comes back over the wire.

**A watch does not survive the node holding it.** If the node that owns the target dies, nothing
arrives — the registration died with it. Losing a node is a cluster-level event and the cluster page
is where it shows up; a per-actor notification would be promising something membership cannot
deliver.

## Dependency injection

```csharp
public sealed class PricingActor(IPriceFeed feed, ILogger<PricingActor> logger) : ReceiveActor
{
    public PricingActor(...) { On<GetPrice>(...); }
}
```

Actors are built through `ActivatorUtilities` when the system has an `IServiceProvider`, so
constructor parameters are resolved from the container. Without one, actors need a parameterless
constructor.

This is the difference between an actor you can unit test and one that reaches for a static.

## Asking an actor what it is holding

```csharp
var inspected = await system.InspectAsync(ActorId.For<WalletActor>("acc-1"));
Console.WriteLine(inspected.State);   // JSON
```

Answering a question about a running actor otherwise means writing a message for it, handling it
and registering both — fine for a question you knew you would ask, useless at three in the morning
for one you did not.

It is an ordinary message: routed by the ring, so it answers for an actor on any node, and handled
on the actor's own loop, so it queues behind whatever that actor is doing and never reads state a
handler is halfway through writing. An actor that does not answer is one that is busy or wedged,
and the timeout says so.

`IInspectable` lets an actor decide what it shows — see
[Tooling](09-tooling.md#actors) for what the console and the HTTP endpoint make of it.

## Concurrency, stated precisely

**Guaranteed:** one activation per address per cluster; one message at a time within an activation;
messages from a single sender to a single actor arrive in order.

**Not guaranteed:** ordering between different senders; ordering across a deactivation boundary
(see [Architecture](02-architecture.md)); delivery at all, if the process dies.

## Next

- [Supervision](04-supervision.md) — what happens when `ReceiveAsync` throws
- [Persistence](05-persistence.md) — making state outlive the activation
