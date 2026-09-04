// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Runtime;

/// <summary>
/// One message in flight, plus the routing metadata the runtime needs to deliver a reply.
/// </summary>
/// <remarks>
/// A struct on purpose: this is written into a channel for every single send, and the local path
/// deliberately carries the <em>materialized</em> <see cref="Message"/> rather than a serialized
/// payload. Only the network transport serializes.
/// </remarks>
public readonly record struct Envelope
{
    /// <summary>Where the message is going.</summary>
    public ActorId Target { get; init; }

    /// <summary>Who sent it, or <see cref="ActorId.None"/>.</summary>
    public ActorId Sender { get; init; }

    /// <summary>The message itself, already materialized.</summary>
    public object Message { get; init; }

    /// <summary>Set when a caller is blocked in AskAsync waiting for a reply.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// The node that owns the pending ask. Null means "this node"; a value means the reply has to
    /// go back over the wire.
    /// </summary>
    public string? ReplyToNode { get; init; }

    /// <summary>
    /// Stopwatch timestamp at enqueue time, so the mailbox loop can report queue latency without
    /// a second clock read on the sending side.
    /// </summary>
    public long EnqueuedTimestamp { get; init; }

    /// <summary>Creates an envelope stamped with the current timestamp.</summary>
    public static Envelope Create(ActorId target, object message, ActorId sender = default,
        string? correlationId = null, string? replyToNode = null) => new()
        {
            Target = target,
            Sender = sender,
            Message = message,
            CorrelationId = correlationId,
            ReplyToNode = replyToNode,
            EnqueuedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
        };
}
