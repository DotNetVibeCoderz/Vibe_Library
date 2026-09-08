# ActorNet.AspNetCore

Generated - edit the `///` comments in the source, not this file.

## ActorNetEndpoints

Registration and endpoints for an actor system living inside an ASP.NET Core application.

### method `AddActorNetCheck(IHealthChecksBuilder, String, HealthStatus)`

Adds the node health check.

Tagged `actornet` and `ready`, so a readiness probe can select it with `Predicate = r => r.Tags.Contains("ready")` while a liveness probe ignores it. The distinction matters: a node that cannot see its peers should stop receiving traffic, and should not be killed and restarted.

### method `MapActorNetDiagnostics(IEndpointRouteBuilder, String)`

Maps read-only diagnostics for this node under `prefix`.

Two endpoints: `/cluster` for the membership and ring ownership as this node sees them, and `/metrics` for its counters. They answer the question the console answers, for a deployment that has no console.

Nothing here is authorized by default, and it exposes node addresses and actor keys. Put it behind whatever the rest of the application uses - the returned builder takes `RequireAuthorization()` like any other group.

### method `RequireActorNetReady<T0>(T0, Int32)`

Refuses requests while this node is not on the ring, with 503 and a `Retry-After`.

For the window between a node deciding it is done - a leave, or losing a partition - and the load balancer noticing. Without it those requests are accepted and then fail somewhere less legible, or worse, are served by a node whose keys have already moved.

## ActorNetHealthCheck

Reports whether this node is serving, and what it can see of the cluster.

A health check that only answered "the process is up" would be worse than none: an orchestrator would keep sending traffic to a node that has lost the cluster and is answering for keys it does not own. What matters is whether this node can still reach the peers it believes exist.

Unreachable peers are *degraded*, not unhealthy. Unreachable means "missed a few beats, still on the ring" - the node is serving its own keys correctly and restarting it would turn a blip into a rebalance. Only a node that has taken itself off the ring is unhealthy.

### method `CheckHealthAsync(HealthCheckContext, CancellationToken)`

---

[Back to the index](README.md)
