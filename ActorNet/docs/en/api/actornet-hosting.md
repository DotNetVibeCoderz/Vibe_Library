# ActorNet.Hosting

Generated - edit the `///` comments in the source, not this file.

## ActorNetBuilder

Collects registrations before the actor system exists, then replays them onto it.

### method `Actor<T0>(SupervisorStrategy)`

Registers an actor type, optionally with its own supervision strategy.

### method `Configure(Action<ActorSystemOptions>)`

Configures the node.

### method `Message<T0>(String)`

Registers a message type for the wire.

### method `MessagesFromAssembly(Assembly)`

Registers every attributed message type in an assembly.

### property `Options`

Node configuration. Mutate it directly.

### property `Serializer`

Overrides the default JSON serializer.

## ActorSystemHostedService

Starts the actor system with the host and stops it on shutdown.

### method `ActorSystemHostedService(ActorSystem, ILogger<ActorSystemHostedService>)`

Starts the actor system with the host and stops it on shutdown.

## ServiceCollectionExtensions

Wires an actor system into a generic host, so it starts and stops with the application.

This is the "deep .NET integration" half of the requirements: the same registration works in a console worker, an ASP.NET Core app and the Blazor dashboard, and actors get constructor injection from the host's container because the system builds them through `ActivatorUtilities`.

### method `AddActorNet(IServiceCollection, Action<ActorNetBuilder>)`

Registers an actor system and starts it with the host.

---

[Back to the index](README.md)
