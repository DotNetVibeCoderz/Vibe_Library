// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet;

/// <summary>
/// A handle to an actor that may live in this process or on another node. Holding one says
/// nothing about where the actor is, or whether it is currently activated.
/// </summary>
public interface IActorRef
{
    /// <summary>The address this reference points at.</summary>
    ActorId Id { get; }

    /// <summary>
    /// Fire-and-forget send. The returned task completes when the message is accepted into the
    /// target's mailbox (or handed to the transport), <em>not</em> when it has been processed.
    /// </summary>
    ValueTask TellAsync(object message, ActorId sender = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Request/response. Completes when the actor calls <see cref="IActorContext.ReplyAsync"/>,
    /// and throws <see cref="AskTimeoutException"/> if it does not do so in time.
    /// </summary>
    Task<TResponse> AskAsync<TResponse>(object message, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
