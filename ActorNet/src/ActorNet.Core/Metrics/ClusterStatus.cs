// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Serialization;

namespace ActorNet.Metrics;

/// <summary>
/// One node's counters, small enough to ask every member for on a refresh.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="ActorSystemSnapshot"/>. That carries a row per active actor, which
/// is the right thing for the node you are standing on and the wrong thing to pull from twenty
/// peers every second - the answer would be larger than everything else on the wire put together.
/// </remarks>
[ActorMessage(Alias = "actornet.node-status")]
public sealed record NodeStatus(
    string NodeId,
    TimeSpan Uptime,
    long MessagesProcessed,
    long MessagesFailed,
    long InFlight,
    int ActiveActors,
    int MailboxDepth,
    long DeadLetters,
    long Restarts,
    long AsksTimedOut,
    double HandlerMicroseconds,
    double QueueMicroseconds)
{
    /// <summary>Reduces a full snapshot to what is worth sending.</summary>
    public static NodeStatus From(ActorSystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new NodeStatus(
            snapshot.NodeId,
            snapshot.Uptime,
            snapshot.MessagesProcessed,
            snapshot.MessagesFailed,
            snapshot.InFlight,
            snapshot.ActiveActors,
            snapshot.MailboxDepth,
            snapshot.DeadLetters,
            snapshot.Restarts,
            snapshot.AsksTimedOut,
            snapshot.AverageProcessingMicroseconds,
            snapshot.AverageQueueLatencyMicroseconds);
    }
}

/// <summary>
/// What every node in the cluster is doing, as far as this one could find out.
/// </summary>
/// <remarks>
/// <para>
/// The ring and the member table have always been cluster-wide; the counters were not, so the
/// console could tell you the cluster had five members and only what one of them was doing. A node
/// under load looks identical to an idle one from four nodes away.
/// </para>
/// <para>
/// <see cref="Silent"/> is part of the answer, not an error. A peer that did not reply in time is
/// a fact about the cluster worth showing next to the ones that did - summing four nodes and
/// presenting it as five would be worse than saying which one is missing.
/// </para>
/// </remarks>
public sealed record ClusterStatus(
    DateTimeOffset TakenAt,
    IReadOnlyList<NodeStatus> Nodes,
    IReadOnlyList<string> Silent)
{
    /// <summary>Messages handled across every node that answered.</summary>
    public long MessagesProcessed => Nodes.Sum(n => n.MessagesProcessed);

    /// <summary>Handlers that threw, across every node that answered.</summary>
    public long MessagesFailed => Nodes.Sum(n => n.MessagesFailed);

    /// <summary>Actors currently activated across the cluster.</summary>
    public int ActiveActors => Nodes.Sum(n => n.ActiveActors);

    /// <summary>Messages accepted and not yet retired, across the cluster.</summary>
    public long InFlight => Nodes.Sum(n => n.InFlight);

    /// <summary>Mailbox depth across the cluster, which is where a struggling node shows first.</summary>
    public int MailboxDepth => Nodes.Sum(n => n.MailboxDepth);

    /// <summary>Undelivered messages across the cluster.</summary>
    public long DeadLetters => Nodes.Sum(n => n.DeadLetters);

    /// <summary>
    /// The node carrying the most in-flight work, or null when nothing answered.
    /// </summary>
    /// <remarks>
    /// The number an operator actually wants from an aggregate. A cluster total hides the case
    /// this exists to find: one node buried while the others idle, which is a placement problem
    /// rather than a capacity one.
    /// </remarks>
    public NodeStatus? Busiest => Nodes.OrderByDescending(n => n.InFlight).FirstOrDefault();
}
