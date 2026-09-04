// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Cluster;
using ActorNet.Metrics;

namespace ActorNet;

/// <summary>
/// The runtime: an actor directory, a mailbox scheduler, a supervisor, a transport and - when
/// clustering is on - a membership view. One per process.
/// </summary>
public interface IActorSystem : IAsyncDisposable
{
    /// <summary>This node's identity within the cluster.</summary>
    string NodeId { get; }

    /// <summary>Starts the transport, the cluster membership and the idle sweeper.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Deactivates every local actor and stops the transport.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Teaches the runtime how to activate an actor type. Required before any message addressed to
    /// that type can be delivered - the type half of an <see cref="ActorId"/> is resolved through
    /// this registry, never through assembly scanning.
    /// </summary>
    IActorSystem RegisterActor<TActor>(SupervisorStrategy? strategy = null) where TActor : IActor;

    /// <summary>Registers a message type for cross-node serialization under an explicit alias.</summary>
    IActorSystem RegisterMessage<TMessage>(string? alias = null);

    /// <summary>
    /// Gets a reference to an actor. Nothing is activated by this call; activation happens on the
    /// first message, and the reference stays valid across deactivation and node moves.
    /// </summary>
    IActorRef ActorOf<TActor>(string key) where TActor : IActor;

    /// <inheritdoc cref="ActorOf{TActor}(string)" />
    IActorRef ActorOf(ActorId id);

    /// <summary>Convenience over <see cref="ActorOf(ActorId)"/> plus a tell.</summary>
    ValueTask TellAsync(ActorId target, object message, ActorId sender = default, CancellationToken cancellationToken = default);

    /// <summary>Convenience over <see cref="ActorOf(ActorId)"/> plus an ask.</summary>
    Task<TResponse> AskAsync<TResponse>(ActorId target, object message, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>Deactivates an actor if it is currently active on this node. Idempotent.</summary>
    Task DeactivateAsync(ActorId id, CancellationToken cancellationToken = default);

    /// <summary>Live counters for the CLI and the dashboard.</summary>
    IMetricsCollector Metrics { get; }

    /// <summary>The cluster view. Present even in single-node mode, where it holds one member.</summary>
    IClusterView Cluster { get; }

    /// <summary>Addresses of the actors currently activated on this node.</summary>
    IReadOnlyCollection<ActorId> LocalActors { get; }
}
