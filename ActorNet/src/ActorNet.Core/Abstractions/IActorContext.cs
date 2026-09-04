// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Microsoft.Extensions.Logging;

namespace ActorNet;

/// <summary>
/// What an actor is handed while processing a message: its own identity, the sender's, and the
/// handful of runtime operations that are only meaningful from inside the mailbox loop.
/// </summary>
public interface IActorContext
{
    /// <summary>This actor's address.</summary>
    ActorId Self { get; }

    /// <summary>
    /// The sender of the message being processed, or <see cref="ActorId.None"/> when it came from
    /// outside the actor system (a client, a stream, or a plain <c>TellAsync</c> with no sender).
    /// </summary>
    ActorId Sender { get; }

    /// <summary>The parent in the supervision tree, or <see cref="ActorId.None"/> for a root actor.</summary>
    ActorId Parent { get; }

    /// <summary>The system this actor is running in.</summary>
    IActorSystem System { get; }

    /// <summary>A logger scoped to this actor.</summary>
    ILogger Logger { get; }

    /// <summary>How many times this actor has been restarted by its supervisor.</summary>
    int RestartCount { get; }

    /// <summary>Sends a message to another actor, stamping this actor as the sender.</summary>
    ValueTask TellAsync(ActorId target, object message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers the message currently being processed. Routes to the waiting
    /// <see cref="IActorRef.AskAsync{TResponse}"/> caller when there is one - across the network if
    /// the ask came from another node - and otherwise to <see cref="Sender"/>.
    /// </summary>
    /// <returns>False when the message had neither a pending ask nor a sender, so the reply went nowhere.</returns>
    ValueTask<bool> ReplyAsync(object message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a child of this actor. The child is supervised by this actor's strategy, and is
    /// stopped when this actor stops.
    /// </summary>
    IActorRef SpawnChild<TActor>(string key, SupervisorStrategy? strategy = null) where TActor : IActor;

    /// <summary>The children spawned by this actor that are still alive.</summary>
    IReadOnlyCollection<ActorId> Children { get; }

    /// <summary>
    /// Schedules a message to this actor after a delay. Survives nothing - a node restart drops
    /// pending timers, so use it for in-activation concerns, not for durable scheduling.
    /// </summary>
    IDisposable ScheduleTell(TimeSpan delay, object message, TimeSpan? repeatEvery = null);

    /// <summary>
    /// Asks the runtime to deactivate this actor once the current message is done. The next
    /// message addressed to it activates a fresh instance.
    /// </summary>
    void DeactivateOnIdle();
}
