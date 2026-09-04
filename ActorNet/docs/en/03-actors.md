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

## Concurrency, stated precisely

**Guaranteed:** one activation per address per cluster; one message at a time within an activation;
messages from a single sender to a single actor arrive in order.

**Not guaranteed:** ordering between different senders; ordering across a deactivation boundary
(see [Architecture](02-architecture.md)); delivery at all, if the process dies.

## Next

- [Supervision](04-supervision.md) — what happens when `ReceiveAsync` throws
- [Persistence](05-persistence.md) — making state outlive the activation
