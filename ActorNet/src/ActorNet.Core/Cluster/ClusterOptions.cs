// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Cluster;

/// <summary>Membership, failure detection and placement settings.</summary>
public sealed class ClusterOptions
{
    /// <summary>
    /// False keeps the node standalone: every key is owned locally and no membership traffic is
    /// sent. A standalone node still has a one-member <see cref="IClusterView"/>, so application
    /// code does not branch on this.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Nodes to contact on startup, as <c>host:port</c>. Any one reachable seed is enough - the
    /// joiner receives the full member table and gossips from there.
    /// </summary>
    public IList<string> Seeds { get; set; } = [];

    /// <summary>How often this node gossips its member table to a peer.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Silence after which a peer is marked <see cref="MemberStatus.Unreachable"/>. Unreachable
    /// members still own their slice of the ring - the assumption is a blip, not a departure.
    /// </summary>
    public TimeSpan UnreachableAfter { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Silence after which a peer is marked <see cref="MemberStatus.Down"/> and removed from the
    /// ring, handing its keys to the remaining nodes. Must be longer than
    /// <see cref="UnreachableAfter"/>, or a brief GC pause would trigger a rebalance.
    /// </summary>
    public TimeSpan DownAfter { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Ring positions per member. Higher spreads the keyspace more evenly at the cost of a bigger
    /// ring to search; 128 keeps a 3-node cluster inside a few percent of even.
    /// </summary>
    public int VirtualNodesPerMember { get; set; } = 128;

    /// <summary>
    /// When membership changes, deactivate local actors whose keys now belong elsewhere. This is
    /// what makes scaling elastic: state is flushed on deactivation and the actor re-activates on
    /// its new owner from the store.
    /// </summary>
    public bool RebalanceOnMembershipChange { get; set; } = true;

    /// <summary>Throws when the settings cannot produce a working cluster.</summary>
    public void Validate()
    {
        if (!Enabled) return;
        if (VirtualNodesPerMember < 1)
            throw new ArgumentOutOfRangeException(nameof(VirtualNodesPerMember), VirtualNodesPerMember, "At least one ring position per member is required.");
        if (DownAfter <= UnreachableAfter)
            throw new ArgumentException(
                $"DownAfter ({DownAfter}) must be longer than UnreachableAfter ({UnreachableAfter}); otherwise a short pause evicts a healthy node and triggers a needless rebalance.",
                nameof(DownAfter));
        if (HeartbeatInterval >= UnreachableAfter)
            throw new ArgumentException(
                $"HeartbeatInterval ({HeartbeatInterval}) must be shorter than UnreachableAfter ({UnreachableAfter}), or a node is declared unreachable before its next beat is due.",
                nameof(HeartbeatInterval));
        foreach (var seed in Seeds)
        {
            if (!TryParseSeed(seed, out _, out _))
                throw new FormatException($"Seed '{seed}' is not in 'host:port' form.");
        }
    }

    /// <summary>Splits a <c>host:port</c> seed.</summary>
    public static bool TryParseSeed(string seed, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (string.IsNullOrWhiteSpace(seed)) return false;

        var split = seed.LastIndexOf(':');
        if (split <= 0 || split == seed.Length - 1) return false;
        if (!int.TryParse(seed[(split + 1)..], out port) || port is < 1 or > 65535) return false;

        host = seed[..split];
        return true;
    }
}
