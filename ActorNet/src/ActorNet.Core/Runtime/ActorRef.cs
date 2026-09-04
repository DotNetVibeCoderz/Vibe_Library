// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Runtime;

/// <summary>
/// A handle to an actor. There is deliberately only one implementation for local and remote
/// actors: routing is decided per message by the hash ring, so a reference cannot be "the local
/// one" - the same address can be local now and remote after the next rebalance.
/// </summary>
internal sealed class ActorRef(ActorSystem system, ActorId id) : IActorRef
{
    /// <inheritdoc />
    public ActorId Id => id;

    /// <inheritdoc />
    public ValueTask TellAsync(object message, ActorId sender = default, CancellationToken cancellationToken = default) =>
        system.TellAsync(id, message, sender, cancellationToken);

    /// <inheritdoc />
    public Task<TResponse> AskAsync<TResponse>(object message, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        system.AskAsync<TResponse>(id, message, timeout, cancellationToken);

    /// <inheritdoc />
    public override string ToString() => id.ToString();
}
