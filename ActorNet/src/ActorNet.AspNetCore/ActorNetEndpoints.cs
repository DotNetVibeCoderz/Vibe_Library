// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.Json;
using ActorNet.Cluster;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ActorNet.AspNetCore;

/// <summary>
/// Registration and endpoints for an actor system living inside an ASP.NET Core application.
/// </summary>
public static class ActorNetEndpoints
{
    /// <summary>
    /// Adds the node health check.
    /// </summary>
    /// <remarks>
    /// Tagged <c>actornet</c> and <c>ready</c>, so a readiness probe can select it with
    /// <c>Predicate = r =&gt; r.Tags.Contains("ready")</c> while a liveness probe ignores it. The
    /// distinction matters: a node that cannot see its peers should stop receiving traffic, and
    /// should not be killed and restarted.
    /// </remarks>
    public static IHealthChecksBuilder AddActorNetCheck(
        this IHealthChecksBuilder builder,
        string name = "actornet",
        HealthStatus failureStatus = HealthStatus.Unhealthy)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddCheck<ActorNetHealthCheck>(name, failureStatus, tags: ["actornet", "ready"]);
    }

    /// <summary>
    /// Maps read-only diagnostics for this node under <paramref name="prefix"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two endpoints: <c>/cluster</c> for the membership and ring ownership as this node sees them,
    /// and <c>/metrics</c> for its counters. They answer the question the console answers, for a
    /// deployment that has no console.
    /// </para>
    /// <para>
    /// Nothing here is authorized by default, and it exposes node addresses and actor keys. Put it
    /// behind whatever the rest of the application uses - the returned builder takes
    /// <c>RequireAuthorization()</c> like any other group.
    /// </para>
    /// </remarks>
    public static RouteGroupBuilder MapActorNetDiagnostics(this IEndpointRouteBuilder endpoints, string prefix = "/actornet")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(prefix);

        group.MapGet("/cluster", (IActorSystem system) =>
        {
            var cluster = system.Cluster;
            var share = cluster.Ring.OwnershipShare();

            return Results.Ok(new
            {
                node = cluster.SelfNodeId,
                singleNode = cluster.IsSingleNode,
                members = cluster.Members.Select(m => new
                {
                    id = m.NodeId,
                    address = m.Address,
                    status = m.Status.ToString(),
                    lastSeen = m.LastSeen,
                    incarnation = m.Incarnation,
                    // Its slice of the keyspace, which is the number that says whether a rebalance
                    // actually landed - a member table alone does not.
                    share = share.TryGetValue(m.NodeId, out var s) ? s : 0d,
                }),
            });
        })
        .WithName("ActorNetCluster");

        group.MapGet("/metrics", (ActorSystem system) =>
        {
            var snapshot = system.Metrics.Snapshot();

            return Results.Ok(new
            {
                node = snapshot.NodeId,
                uptime = snapshot.Uptime,
                processed = snapshot.MessagesProcessed,
                failed = snapshot.MessagesFailed,
                inFlight = snapshot.InFlight,
                activeActors = snapshot.ActiveActors,
                mailboxDepth = snapshot.MailboxDepth,
                deadLetters = snapshot.DeadLetters,
                restarts = snapshot.Restarts,
                asksTimedOut = snapshot.AsksTimedOut,
                handlerMicroseconds = snapshot.AverageProcessingMicroseconds,
                queueMicroseconds = snapshot.AverageQueueLatencyMicroseconds,
            });
        })
        .WithName("ActorNetMetrics");

        group.MapGet("/cluster-status", async (ActorSystem system) =>
        {
            var status = await system.GetClusterStatusAsync(TimeSpan.FromSeconds(2));

            return Results.Ok(new
            {
                takenAt = status.TakenAt,
                totals = new
                {
                    processed = status.MessagesProcessed,
                    failed = status.MessagesFailed,
                    inFlight = status.InFlight,
                    activeActors = status.ActiveActors,
                    mailboxDepth = status.MailboxDepth,
                    deadLetters = status.DeadLetters,
                },
                busiest = status.Busiest?.NodeId,

                // Named rather than omitted: totals over four nodes presented as five would be
                // worse than saying which one did not answer.
                silent = status.Silent,
                nodes = status.Nodes,
            });
        })
        .WithName("ActorNetClusterStatus");

        group.MapGet("/actors/{type}/{key}", async (string type, string key, ActorSystem system) =>
        {
            try
            {
                var inspected = await system.InspectAsync(new ActorId(type, key), TimeSpan.FromSeconds(5));

                // The state is already JSON, so it is written through rather than re-encoded: a
                // string of JSON nested inside JSON is the thing every reader then has to undo.
                using var document = inspected.State is null ? null : JsonDocument.Parse(inspected.State);

                return Results.Ok(new
                {
                    actor = inspected.Actor,
                    type = inspected.ActorType,
                    messagesHandled = inspected.MessagesHandled,
                    activatedAt = inspected.ActivatedAt,
                    state = document?.RootElement.Clone(),
                });
            }
            catch (ActorTypeNotRegisteredException)
            {
                // A type this node does not know is a 404 rather than a 500: the caller asked for
                // something that does not exist here, which is a fact about the request.
                return Results.NotFound(new { error = $"No actor type named '{type}' is registered on this node." });
            }
            catch (AskTimeoutException)
            {
                // An actor that does not answer is usually one that is busy, and an inspection
                // queues behind whatever it is doing. Saying so beats a generic failure.
                return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
            }
        })
        .WithName("ActorNetInspect");

        return group;
    }

    /// <summary>
    /// Refuses requests while this node is not on the ring, with 503 and a <c>Retry-After</c>.
    /// </summary>
    /// <remarks>
    /// For the window between a node deciding it is done - a leave, or losing a partition - and the
    /// load balancer noticing. Without it those requests are accepted and then fail somewhere less
    /// legible, or worse, are served by a node whose keys have already moved.
    /// </remarks>
    public static TBuilder RequireActorNetReady<TBuilder>(this TBuilder builder, int retryAfterSeconds = 5)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(endpoint => endpoint.FilterFactories.Add((_, next) => async context =>
        {
            var system = context.HttpContext.RequestServices.GetRequiredService<IActorSystem>();
            var self = system.Cluster.Members.FirstOrDefault(m => m.NodeId == system.Cluster.SelfNodeId);

            if (self is not null && self.Status is MemberStatus.Down or MemberStatus.Leaving)
            {
                context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            return await next(context);
        }));

        return builder;
    }
}
