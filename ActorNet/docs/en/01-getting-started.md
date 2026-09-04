# Getting started

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[Bahasa Indonesia](../id/01-memulai.md) · [Docs index](README.md)*

## Requirements

.NET 10 SDK. Nothing else — the library depends only on the `Microsoft.Extensions.*` abstractions,
so it will not drag a logging or DI implementation into your application.

## Install

```bash
dotnet add package ActorNet
```

Optionally, the console tooling:

```bash
dotnet tool install -g ActorNet.Cli
```

## A first actor

Three pieces: a message, an actor, and a system to run it in.

```csharp
using ActorNet;

// Messages are records. They need to survive a JSON round trip only if they cross a node
// boundary; in-process sends carry the object itself.
public sealed record Greet(string Name);
public sealed record Greeted(string Text, int TimesGreeted);

public sealed class GreeterActor : ReceiveActor
{
    private int _count;

    public GreeterActor()
    {
        On<Greet>(async (message, ct) =>
        {
            _count++;
            await Context.ReplyAsync(new Greeted($"Hello, {message.Name}", _count), ct);
        });
    }
}
```

`_count` needs no lock. One activation handles one message at a time, which is the guarantee the
whole framework is built to give you.

```csharp
var system = new ActorSystem(new ActorSystemOptions
{
    NodeId = "node-1",
    EnableNetworking = false,   // in-process only; no socket is opened
});

system.RegisterActor<GreeterActor>();
await system.StartAsync();

var greeter = system.ActorOf<GreeterActor>("front-desk");

await greeter.TellAsync(new Greet("Fadhil"));
var reply = await greeter.AskAsync<Greeted>(new Greet("Sari"));

Console.WriteLine(reply.Text);          // Hello, Sari
Console.WriteLine(reply.TimesGreeted);  // 2

await system.DisposeAsync();
```

Note what did *not* happen: nothing constructed the actor. `ActorOf` returns a reference to an
address; the first message activates it. That is what "virtual" means here.

## Registration is required

```csharp
system.RegisterActor<GreeterActor>();
```

Without it, a send throws `ActorTypeNotRegisteredException`. This is deliberate. The alternative —
scanning assemblies for a matching type name — means a peer on the network can name any type in
your process and have it constructed, which is the shape of every deserialization exploit. An
unregistered type is a wiring bug, and throwing surfaces it where it happened rather than turning
it into a message that silently vanished.

## Tell and ask

```csharp
await greeter.TellAsync(new Greet("Budi"));                     // fire and forget
var reply = await greeter.AskAsync<Greeted>(new Greet("Budi")); // wait for a reply
```

`TellAsync` completes when the message is **accepted into the mailbox**, not when it has been
handled. That is what makes it cheap, and it is the thing to remember when writing a test: a tell
followed immediately by an assertion is a race.

`AskAsync` waits for the actor to call `Context.ReplyAsync`, and throws `AskTimeoutException` if it
never does. Because an ask is ordered behind everything already in the mailbox, it doubles as a
barrier:

```csharp
for (var i = 0; i < 1000; i++) await greeter.TellAsync(new Greet("x"));

// This reply cannot arrive until all 1,000 above have been handled.
var settled = await greeter.AskAsync<Greeted>(new Greet("last"));
```

## Adding persistence

Change the base class and the state survives deactivation:

```csharp
public sealed class GreeterState
{
    public int Count { get; set; }
}

public sealed class GreeterActor : PersistentActor<GreeterState>
{
    protected override Task ReceiveAsync(object message, CancellationToken ct) => message switch
    {
        Greet greet => Handle(greet, ct),
        _ => Task.CompletedTask,
    };

    private async Task Handle(Greet greet, CancellationToken ct)
    {
        State.Count++;
        await Context.ReplyAsync(new Greeted($"Hello, {greet.Name}", State.Count), ct);
    }
}
```

State loads on activation and flushes on deactivation. The default store is in memory, which
survives deactivation but not a process restart — swap it for a file store when you want the
latter:

```csharp
options.StateStore = new FileStateStore("./data/state");
```

See [Persistence](05-persistence.md) for the event-sourced model, which keeps history rather than
the current value.

## Adding a second node

```csharp
var options = new ActorSystemOptions
{
    NodeId = "node-2",
    Port = 9001,
    EnableNetworking = true,
};

options.Cluster.Enabled = true;
options.Cluster.Seeds = ["127.0.0.1:9000"];
```

The first node of a cluster has no seeds of its own, so it needs `Cluster.Enabled = true` with an
empty `Seeds` list — or `--cluster` on the CLI. Without that it runs standalone and never gossips,
and its peers eventually mark a perfectly healthy node unreachable.

From then on, nothing in your calling code changes. `TellAsync` asks the hash ring who owns the key
and either enqueues locally or hands the message to the transport.

## Hosting in an application

```csharp
builder.Services.AddActorNet(actors =>
{
    actors.Options.NodeId = "api-1";
    actors.Options.Port = 9000;
    actors.Actor<GreeterActor>();
    actors.Message<Greet>();
    actors.Message<Greeted>();
});
```

The system starts and stops with the host, and actors are constructed through the container — so
they can take a repository or an `HttpClient` as a constructor parameter.

## Where to next

- [Architecture](02-architecture.md) — what happens between a send and a handler
- [Actors and lifecycle](03-actors.md) — activation, deactivation, children
- [Supervision](04-supervision.md) — what happens when a handler throws
- [Tooling](09-tooling.md) — the CLI, the console, the samples
