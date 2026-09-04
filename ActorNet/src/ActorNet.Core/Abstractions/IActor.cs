// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet;

/// <summary>
/// The contract the runtime drives. Application code normally derives from
/// <see cref="VirtualActor"/> rather than implementing this directly.
/// </summary>
/// <remarks>
/// Every method here is called from the actor's own mailbox loop, one at a time, so an
/// implementation never needs a lock over its own fields. The runtime guarantees
/// <see cref="OnActivateAsync"/> completes before the first <see cref="ReceiveAsync"/>.
/// </remarks>
public interface IActor
{
    /// <summary>Called once before any message is delivered. Load persistent state here.</summary>
    Task OnActivateAsync(IActorContext context, CancellationToken cancellationToken);

    /// <summary>Handles one message.</summary>
    Task ReceiveAsync(IActorContext context, object message, CancellationToken cancellationToken);

    /// <summary>
    /// Called once when the actor is going away - idle timeout, explicit stop, supervision
    /// decision, or node shutdown. Flush state here.
    /// </summary>
    Task OnDeactivateAsync(IActorContext context, DeactivationReason reason, CancellationToken cancellationToken);

    /// <summary>
    /// Called after a supervisor decided to restart this actor, before the replacement is
    /// activated. The failing instance gets this; the replacement gets <see cref="OnActivateAsync"/>.
    /// </summary>
    Task OnRestartAsync(IActorContext context, Exception cause, CancellationToken cancellationToken);
}

/// <summary>Why an actor is being deactivated. Surfaced to <see cref="IActor.OnDeactivateAsync"/>.</summary>
public enum DeactivationReason
{
    /// <summary>No message arrived within the configured idle timeout - the normal case.</summary>
    Idle,

    /// <summary>Application code asked for it, via the context or the system.</summary>
    Requested,

    /// <summary>A supervisor decided to stop this actor after a failure.</summary>
    Supervision,

    /// <summary>The cluster moved ownership of this key to another node.</summary>
    Rebalanced,

    /// <summary>The node is shutting down.</summary>
    Shutdown,
}
