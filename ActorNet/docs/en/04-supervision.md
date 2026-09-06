# Supervision

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[Bahasa Indonesia](../id/04-supervisi.md) · [Docs index](README.md)*

## The idea

When a handler throws, the actor does not crash the process and does not silently swallow it.
A **supervisor** decides what happens, and that decision is configuration rather than something the
actor codes around.

The `Supervision` scenario in the desktop samples makes this concrete: four actors of the *same
class*, registered with different strategies, given the same exception.

![Supervision, four outcomes for one exception](../images/samples-supervision.png)

Resume kept its total. Restart went back to zero with one restart recorded. Stop was deactivated.
The budgeted one restarted but will stop rather than restart forever.

## The four directives

| Directive | What happens | Use it when |
| --- | --- | --- |
| `Resume` | Drop the offending message; keep the instance and its state | The message was bad, the actor is fine |
| `Restart` | Rebuild the instance. Address, mailbox and children survive; in-memory state does not | The actor's state may be corrupt |
| `Stop` | Deactivate. The next message activates a fresh instance | The failure is fatal or repeats identically |
| `Escalate` | Stop this actor and let the parent's strategy decide | This actor cannot judge the failure |

## Attaching a strategy

```csharp
system.RegisterActor<PaymentActor>(new OneForOneStrategy(ex => ex switch
{
    InsufficientFundsException => Directive.Resume,
    HttpRequestException       => Directive.Restart,
    TimeoutException           => Directive.Restart,
    _                          => Directive.Escalate,
})
{
    MaxRestarts = 5,
    Window = TimeSpan.FromMinutes(1),
});
```

Actors registered without one get `Options.DefaultSupervisorStrategy`.

## The built-in strategies

```csharp
SupervisorStrategy.Default          // restart, but stop on a wiring bug
SupervisorStrategy.StopOnFailure    // any failure deactivates
SupervisorStrategy.ResumeOnFailure  // log and skip the message, keep state
```

`Default` is worth reading:

```csharp
ex switch
{
    ActorTypeNotRegisteredException => Directive.Stop,
    UnknownMessageTypeException     => Directive.Stop,
    FormatException                 => Directive.Stop,
    _                               => Directive.Restart,
}
```

The split is deliberate. A missing registration or a malformed address will fail identically every
time, so restarting is a busy-loop. Everything else — a transient database error, a bad payload —
gets a fresh instance, which is the whole point of a supervisor.

## The restart budget

```csharp
new OneForOneStrategy(_ => Directive.Restart)
{
    MaxRestarts = 10,
    Window = TimeSpan.FromMinutes(1),
}
```

Exceed `MaxRestarts` within a sliding `Window` and the directive is downgraded to `Stop`.

This is not a nicety. Without it, a poison message sitting at the head of a mailbox buys a fresh
instance forever and burns a core doing it. With it, the actor goes away and the problem becomes
visible as an address that stopped responding.

## Backoff between restarts

The budget bounds a crash loop. It does not slow one down, and ten restarts inside a millisecond is
ten connection attempts at a database that is already struggling — which is usually why the actor
failed in the first place.

```csharp
new OneForOneStrategy(_ => Directive.Restart)
{
    MinBackoff = TimeSpan.FromMilliseconds(100),   // the second restart waits this long
    MaxBackoff = TimeSpan.FromSeconds(5),          // and the doubling stops here
    BackoffJitter = 0.2,                           // spread either side, so failures do not sync up
}
```

**The first restart is immediate.** The common failure is a one-off — a timeout, a malformed
payload — and making every actor pay for the rare crash loop would be the wrong trade. It is the
second failure in quick succession that says the cause has not gone away.

From there the wait doubles: 100ms, 200ms, 400ms, up to `MaxBackoff`. The ceiling is deliberately
well short of `Window`; if the waits added up to more than the window it would keep resetting, and a
permanently broken actor would restart forever instead of hitting `MaxRestarts` and stopping.

**Jitter matters more than it looks.** A database going down fails every actor that touches it
inside the same millisecond. Without jitter they all come back in lockstep and hit it together,
which is how a brief outage becomes a long one.

The wait happens on the actor's own mailbox loop, so its messages queue while it is down and no
other actor is held up. Set `MinBackoff` to zero to get the old behaviour, where a restart is
immediate however many have come before it.

## Scope: one-for-one and all-for-one

```csharp
new OneForOneStrategy(...)   // only the failing actor is affected — the default
new AllForOneStrategy(...)   // every sibling under the same parent is affected too
```

All-for-one is right when siblings share an invariant — a set of shard workers that only make sense
restarted as a group. It only applies to actors that *have* a parent: at the root every actor would
count as a sibling, and restarting the node because one actor threw is never what was meant.

## Escalation

```csharp
new OneForOneStrategy(_ => Directive.Escalate)
```

The child is stopped — it declined to handle its own failure, so it is not fit to continue — and
the parent's strategy is consulted about the same exception. If the parent also escalates, it goes
up again. At the root the failure is logged as `ActorFailureEscalatedException` and the actor stays
stopped.

## What a restart actually does

```
OnRestartAsync(cause)     ← on the failing instance; last chance to release something
  ↓
a new instance is constructed
  ↓
OnActivateAsync           ← on the new instance; reload state here
```

Preserved: the address, the mailbox and its queued messages, the children, `RestartCount`.
Lost: everything in the old instance's fields.

For a `PersistentActor` the new instance reloads from the store, so a restart is close to
transparent. For an in-memory actor it is a reset — which is exactly what the supervision sample
shows.

## Failures and asks

An actor that throws while handling a message someone is asking on does **not** leave the caller on
a timeout. The pending ask is completed with the failure:

```csharp
try
{
    await system.AskAsync<Receipt>(id, new Charge(50m));
}
catch (ActorNetException ex)
{
    // ex.InnerException is what the handler threw.
}
```

A caller that gets an exception knows what went wrong. A caller that gets a timeout only knows it
waited, which is by far the harder bug to chase. This works across nodes too, over an
`AskFailure` frame.

## Failures and persistence

`PersistentActor` skips its flush when the deactivation reason is `Supervision`:

```csharp
case Boom:
    State.Balance += 999m;          // mutated
    throw new InvalidOperationException();   // …then threw
```

Writing that back would turn a transient bug into permanently wrong data. There is a test for
exactly this.

## Choosing a strategy

Two questions:

**Could this actor's state be wrong now?** If the handler mutates state before it can throw,
`Resume` keeps the damage. Prefer `Restart`, or move the mutation after everything that can fail.

**Will retrying help?** A network blip: yes, restart. A malformed message that will be re-read on
every activation: no, `Stop` — and look at where it came from.

A reasonable default for a service:

```csharp
new OneForOneStrategy(ex => ex switch
{
    // Transient: worth a fresh instance.
    TimeoutException or HttpRequestException or IOException => Directive.Restart,

    // The message is bad, not the actor.
    ArgumentException or FormatException or JsonException => Directive.Resume,

    // Unknown: rebuild rather than assume the state survived.
    _ => Directive.Restart,
})
{
    MaxRestarts = 10,
    Window = TimeSpan.FromMinutes(1),
}
```

## What is not here yet

- **Backoff between restarts.** They are immediate; only the budget limits them.
- **Watch / `Terminated` notifications.** An actor cannot subscribe to another's death.

Both are on the [roadmap](../../Plan.md).

## Next

- [Persistence](05-persistence.md)
- [Troubleshooting](11-troubleshooting.md)
