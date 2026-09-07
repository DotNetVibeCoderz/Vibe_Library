// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Serialization;

namespace ActorNet;

/// <summary>
/// Asks an actor what its state currently is.
/// </summary>
/// <remarks>
/// <para>
/// Answering questions about an actor otherwise means writing a message for the purpose, handling
/// it, and registering both - which is fine for a question you knew you would ask and useless at
/// three in the morning for one you did not. Every actor answers this one.
/// </para>
/// <para>
/// It is read on the actor's own mailbox loop, like everything else about an activation, so it
/// never sees state half-written by a handler that is still running. The cost is that it queues
/// behind whatever that actor is already doing: an actor that is wedged does not answer, which is
/// itself worth knowing.
/// </para>
/// </remarks>
[ActorMessage(Alias = "actornet.inspect")]
public sealed record Inspect;

/// <summary>What an actor is holding, as JSON.</summary>
/// <param name="Actor">The actor, in <c>Type/Key</c> form.</param>
/// <param name="ActorType">The runtime type of the instance, which is not always the addressed type.</param>
/// <param name="State">
/// The state, as JSON. Null when the actor keeps nothing this can reach.
/// </param>
/// <param name="MessagesHandled">How many messages this activation has handled.</param>
/// <param name="ActivatedAt">When this activation started - not when the actor first existed.</param>
[ActorMessage(Alias = "actornet.inspected")]
public sealed record Inspected(
    string Actor,
    string ActorType,
    string? State,
    long MessagesHandled,
    DateTimeOffset ActivatedAt);

/// <summary>
/// Implement to decide what <see cref="Inspect"/> reports for an actor.
/// </summary>
/// <remarks>
/// The default reflects over the instance, which is right for most actors and wrong for one
/// holding a password, a token or half a gigabyte of cache. An actor that implements this says
/// what it wants seen instead - and returning null says "nothing".
/// </remarks>
public interface IInspectable
{
    /// <summary>The state to report, or null to report none.</summary>
    object? Inspect();
}
