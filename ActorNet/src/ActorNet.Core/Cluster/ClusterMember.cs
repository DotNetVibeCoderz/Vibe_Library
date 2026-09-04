// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Cluster;

/// <summary>Where a peer stands in this node's view of the cluster.</summary>
public enum MemberStatus
{
    /// <summary>Seen, but has not completed a join handshake yet. Not on the ring.</summary>
    Joining,

    /// <summary>Healthy and carrying its share of the keyspace.</summary>
    Up,

    /// <summary>Missed heartbeats. Still on the ring - the bet is that it comes back.</summary>
    Unreachable,

    /// <summary>Off the ring. Its keys have been redistributed.</summary>
    Down,

    /// <summary>Shutting down gracefully; it has asked to be taken off the ring.</summary>
    Leaving,
}

/// <summary>One node as seen from another.</summary>
/// <param name="NodeId">Stable identity. This, not the address, is what the ring hashes.</param>
/// <param name="Host">Host the node's transport listens on.</param>
/// <param name="Port">Port the node's transport listens on.</param>
/// <param name="Status">Current status in the observer's view.</param>
/// <param name="LastSeen">When the observer last had evidence this node was alive.</param>
/// <param name="Incarnation">
/// Monotonic per-node counter used to break gossip ties. A node that restarts comes back with a
/// higher incarnation, so its own view of itself always beats a stale peer's memory of it.
/// </param>
public sealed record ClusterMember(
    string NodeId,
    string Host,
    int Port,
    MemberStatus Status,
    DateTimeOffset LastSeen,
    long Incarnation)
{
    /// <summary>Address in <c>host:port</c> form.</summary>
    public string Address => $"{Host}:{Port}";

    /// <summary>True when this member should be placed on the hash ring.</summary>
    public bool IsRoutable => Status is MemberStatus.Up or MemberStatus.Unreachable;
}

/// <summary>Read-only view of cluster membership and key placement.</summary>
public interface IClusterView
{
    /// <summary>The observing node.</summary>
    string SelfNodeId { get; }

    /// <summary>True when clustering is off, or on but this is the only member.</summary>
    bool IsSingleNode { get; }

    /// <summary>Every member this node knows about, including itself.</summary>
    IReadOnlyList<ClusterMember> Members { get; }

    /// <summary>
    /// The placement ring as this node currently sees it. Exposed so tooling can show ownership
    /// rather than infer it by sampling keys.
    /// </summary>
    HashRing Ring { get; }

    /// <summary>Which node owns an actor key, per the consistent-hash ring.</summary>
    string OwnerOf(ActorId id);

    /// <summary>True when this node owns the key and should activate it locally.</summary>
    bool IsLocal(ActorId id);

    /// <summary>Raised after the member table changes, on a background thread.</summary>
    event Action<IReadOnlyList<ClusterMember>>? MembershipChanged;
}
