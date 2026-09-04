// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet;

/// <summary>
/// The base class application actors derive from.
/// </summary>
/// <remarks>
/// <para>
/// "Virtual" means the same thing it does in Orleans: an actor always exists conceptually, is
/// activated by the runtime on its first message, and is deactivated again once it has been idle.
/// Nothing creates or destroys one explicitly, and a reference stays valid across both.
/// </para>
/// <para>
/// The <see cref="IActor"/> members are implemented explicitly and forwarded to the protected
/// overloads below, which do not take a context - it is already on <see cref="Context"/>. That
/// keeps the runtime's contract out of the shape a user writes against.
/// </para>
/// <para>
/// One message is processed at a time, so fields need no synchronization. The corollary is that
/// <see cref="Context"/> describes the message currently being handled: capturing it into a
/// background task and using it later reads someone else's sender.
/// </para>
/// </remarks>
public abstract class VirtualActor : IActor
{
    /// <summary>Identity, sender and runtime operations for the message being handled.</summary>
    protected IActorContext Context { get; private set; } = null!;

    Task IActor.OnActivateAsync(IActorContext context, CancellationToken cancellationToken)
    {
        Context = context;
        return OnActivateAsync(cancellationToken);
    }

    Task IActor.ReceiveAsync(IActorContext context, object message, CancellationToken cancellationToken)
    {
        Context = context;
        return ReceiveCoreAsync(message, cancellationToken);
    }

    /// <summary>
    /// The runtime's entry point into a message, wrapping <see cref="ReceiveAsync"/>.
    /// </summary>
    /// <remarks>
    /// Base classes that need to run something around every message - persistence checkpointing,
    /// tracing - override this rather than the abstract <see cref="ReceiveAsync"/>, which belongs
    /// to the application actor at the bottom of the hierarchy.
    /// </remarks>
    protected virtual Task ReceiveCoreAsync(object message, CancellationToken cancellationToken) =>
        ReceiveAsync(message, cancellationToken);

    Task IActor.OnDeactivateAsync(IActorContext context, DeactivationReason reason, CancellationToken cancellationToken)
    {
        Context = context;
        return OnDeactivateAsync(reason, cancellationToken);
    }

    Task IActor.OnRestartAsync(IActorContext context, Exception cause, CancellationToken cancellationToken)
    {
        Context = context;
        return OnRestartAsync(cause, cancellationToken);
    }

    /// <summary>Runs once before the first message. Load state here.</summary>
    protected virtual Task OnActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Handles one message.</summary>
    protected abstract Task ReceiveAsync(object message, CancellationToken cancellationToken);

    /// <summary>Runs once on the way out. Flush state here.</summary>
    protected virtual Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Runs on the failing instance after a supervisor chose to restart it.</summary>
    protected virtual Task OnRestartAsync(Exception cause, CancellationToken cancellationToken) => Task.CompletedTask;
}
