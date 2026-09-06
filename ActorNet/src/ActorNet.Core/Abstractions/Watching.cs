// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Serialization;

namespace ActorNet;

/// <summary>
/// Asks an actor to report when it stops. Sent by <see cref="IActorContext.WatchAsync"/>.
/// </summary>
/// <remarks>
/// An ordinary message rather than a new frame kind, so it travels the path every other message
/// takes - routed to whichever node owns the target, serialized through the same allow-list. The
/// receiving cell handles it itself; the actor never sees it.
/// </remarks>
/// <param name="Watcher">The watching actor, in <c>Type/Key</c> form.</param>
[ActorMessage(Alias = "actornet.watch")]
public sealed record Watch(string Watcher);

/// <summary>
/// Asks the receiving node to activate an actor now, rather than on the first message.
/// </summary>
/// <remarks>
/// <para>
/// Sent by a node that is shutting down, to whoever inherits its keys. Rebalancing works by
/// deactivating an actor and letting the next message reactivate it from the store, so the first
/// message to every key that moved pays a read. On a rolling restart that is every actor the node
/// held, all at once, at the moment traffic arrives.
/// </para>
/// <para>
/// The receiving cell handles this itself and the actor never sees it. Activation is the entire
/// point of the message; there is nothing else to do with it.
/// </para>
/// </remarks>
[ActorMessage(Alias = "actornet.warm")]
public sealed record Warm;

/// <summary>Withdraws a <see cref="Watch"/>.</summary>
/// <param name="Watcher">The actor that no longer wants to be told.</param>
[ActorMessage(Alias = "actornet.unwatch")]
public sealed record Unwatch(string Watcher);

/// <summary>
/// Tells a watcher that the actor it was watching has stopped and will not come back on its own.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately narrower than Akka's <c>Terminated</c>, because a virtual actor's lifecycle is not
/// Akka's. Deactivating after an idle timeout, moving to another node during a rebalance, and going
/// down with a node that is shutting down are all routine here: the address stays valid and the
/// next message brings the actor back, so reporting them as terminations would be reporting
/// bookkeeping as bereavement.
/// </para>
/// <para>
/// What is worth being told about is an actor that stopped for a reason: a supervisor gave up on
/// it, or something asked it to stop. Those are the two reasons that arrive here.
/// </para>
/// </remarks>
/// <param name="Actor">The actor that stopped, in <c>Type/Key</c> form.</param>
/// <param name="Reason">Why it stopped.</param>
[ActorMessage(Alias = "actornet.terminated")]
public sealed record Terminated(string Actor, DeactivationReason Reason)
{
    /// <summary>The actor that stopped.</summary>
    public ActorId Id => ActorId.Parse(Actor);
}
