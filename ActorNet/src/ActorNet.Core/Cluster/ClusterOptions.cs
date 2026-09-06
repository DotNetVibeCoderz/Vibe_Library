// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Cluster;

/// <summary>What a node does when it can only see part of the cluster.</summary>
public enum SplitBrainStrategy
{
    /// <summary>
    /// Nothing. Both sides of a partition keep serving, which means two activations of the same
    /// actor and two divergent versions of its state.
    /// </summary>
    /// <remarks>
    /// The default, because the alternative is a node shutting itself down on its own judgement,
    /// and whether availability or consistency is the thing to protect is not a framework's call.
    /// </remarks>
    None,

    /// <summary>
    /// The side that can still see more than half of the last agreed membership survives; the
    /// other side takes itself down.
    /// </summary>
    /// <remarks>
    /// An even split is settled by the lowest node id, so a two-node cluster does not lose both
    /// halves. Membership counts against the last state everyone agreed on, not against what each
    /// side can currently see - otherwise each side would count itself a majority of itself.
    /// </remarks>
    KeepMajority,

    /// <summary>
    /// A side survives only if it can see at least <see cref="ClusterOptions.StaticQuorumSize"/>
    /// members.
    /// </summary>
    /// <remarks>
    /// The right choice when the cluster size is fixed and known, and safer than a majority when
    /// nodes are added and removed often: a majority of a membership that has itself drifted is
    /// not the guarantee it looks like.
    /// </remarks>
    StaticQuorum,
}

/// <summary>How a peer's silence is turned into a verdict.</summary>
public enum FailureDetection
{
    /// <summary>
    /// Adaptive: suspicion grows against how long this peer's beats have actually been taking.
    /// </summary>
    PhiAccrual,

    /// <summary>
    /// A flat deadline on last contact. Predictable, and necessarily set for the worst link.
    /// </summary>
    Deadline,
}

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
    /// What this node does when it can only see part of the cluster.
    /// </summary>
    /// <remarks>
    /// Off by default. Turning it on means a node may shut itself down without being asked, which
    /// is the correct answer when divergent state would be worse than lost capacity - and the wrong
    /// one when it would not.
    /// </remarks>
    public SplitBrainStrategy SplitBrainStrategy { get; set; } = SplitBrainStrategy.None;

    /// <summary>Members a side must be able to see to survive, under <see cref="SplitBrainStrategy.StaticQuorum"/>.</summary>
    /// <remarks>
    /// Set it above half the intended cluster size, or two sides can both reach it and the strategy
    /// protects nothing.
    /// </remarks>
    public int StaticQuorumSize { get; set; }

    /// <summary>
    /// How long the set of reachable members must hold still before a partition is acted on.
    /// </summary>
    /// <remarks>
    /// A partition is rarely a clean cut: nodes drop out over several seconds, and deciding on the
    /// first observation would mean deciding against a membership that is still moving. Waiting
    /// costs the availability of the losing side for this long, and buys deciding once.
    /// </remarks>
    public TimeSpan SplitBrainStabilityWindow { get; set; } = TimeSpan.FromSeconds(7);

    /// <summary>
    /// Whether a seed given as a hostname is expanded to every address it resolves to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Kubernetes headless service resolves to one address per pod. Connecting to the name gets
    /// whichever record the resolver happened to return first, so a single seed entry reaches one
    /// pod - and if that pod is the one still starting, the join fails and the node waits for its
    /// next beat to try the same coin flip again.
    /// </para>
    /// <para>
    /// With this on, one seed entry means every replica behind the name, and a cluster's whole
    /// configuration is the service name. Addresses are re-resolved on each attempt, so pods that
    /// appear later are picked up without a restart.
    /// </para>
    /// </remarks>
    public bool ResolveSeedHostnames { get; set; } = true;

    /// <summary>
    /// How many peers this node gossips to per beat. Zero means every peer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Gossiping to everybody costs O(members squared) frames per interval, which is nothing at ten
    /// nodes and ruinous at a hundred. A fanout spreads the same information epidemically at
    /// O(members x fanout), converging in about log(members) rounds instead of one.
    /// </para>
    /// <para>
    /// Peers are taken in a rotation rather than at random. A random subset makes the gap between
    /// two particular nodes a coin flip, and the failure detector then has to be tolerant of a
    /// silence that was merely unlucky. A rotation makes that gap exactly
    /// <c>ceil(peers / fanout)</c> beats, which is a number the detector can learn.
    /// </para>
    /// </remarks>
    public int GossipFanout { get; set; } = 4;

    /// <summary>
    /// How long the startup handshake may spend on its seeds before the node comes up anyway.
    /// </summary>
    /// <remarks>
    /// A seed behind a firewall that drops packets rather than refusing them is not refused: the
    /// connect sits there until the OS gives up, which is a minute or more. Without a deadline that
    /// is a minute of a node not starting. Coming up alone is safe, because the handshake is
    /// retried on every beat until a peer answers.
    /// </remarks>
    public TimeSpan JoinTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How a peer's silence is judged.
    /// </summary>
    /// <remarks>
    /// <see cref="FailureDetection.PhiAccrual"/> is the default because a fixed deadline has to be
    /// set for the worst link in the cluster and is then too slow on all the others.
    /// <see cref="FailureDetection.Deadline"/> remains for the case where a flat, stated number is
    /// worth more than accuracy - a test that wants a node down at a known second, most of all.
    /// </remarks>
    public FailureDetection FailureDetection { get; set; } = FailureDetection.PhiAccrual;

    /// <summary>
    /// Suspicion at which a peer is marked <see cref="MemberStatus.Unreachable"/>, in phi.
    /// </summary>
    /// <remarks>
    /// Phi is a logarithm: 8 means the detector expects to be wrong about once in 10^8 given how
    /// this peer normally behaves. Lowering it notices failures sooner and cries wolf more often.
    /// </remarks>
    public double PhiUnreachableThreshold { get; set; } = 8.0;

    /// <summary>Suspicion at which a peer is taken off the ring, in phi. Must exceed the other one.</summary>
    public double PhiDownThreshold { get; set; } = 12.0;

    /// <summary>
    /// Slack added to a peer's average interval before suspicion begins.
    /// </summary>
    /// <remarks>
    /// This is the stop-the-world allowance. On a link whose beats are metronomic the measured
    /// spread is tiny, and without slack a GC pause a fraction of a second past the usual interval
    /// would read as a failure.
    /// </remarks>
    public TimeSpan AcceptableHeartbeatPause { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>A floor on the measured spread of a peer's heartbeat intervals.</summary>
    /// <remarks>
    /// Without a floor, a peer that has been perfectly regular gets a standard deviation near zero,
    /// and phi jumps from nothing to enormous a millisecond past its usual interval.
    /// </remarks>
    public TimeSpan MinimumStandardDeviation { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>How many heartbeat intervals the detector remembers per peer.</summary>
    public int HeartbeatSampleSize { get; set; } = 200;

    /// <summary>
    /// Silence after which a peer is marked <see cref="MemberStatus.Unreachable"/>. Unreachable
    /// members still own their slice of the ring - the assumption is a blip, not a departure.
    /// </summary>
    /// <remarks>Used when <see cref="FailureDetection"/> is <see cref="FailureDetection.Deadline"/>.</remarks>
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
        if (PhiUnreachableThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(PhiUnreachableThreshold), PhiUnreachableThreshold, "A threshold of zero suspects every peer immediately.");
        if (PhiDownThreshold <= PhiUnreachableThreshold)
            throw new ArgumentException(
                $"PhiDownThreshold ({PhiDownThreshold}) must exceed PhiUnreachableThreshold ({PhiUnreachableThreshold}); otherwise a peer is taken off the ring without ever being given the benefit of the doubt.",
                nameof(PhiDownThreshold));
        if (MinimumStandardDeviation <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MinimumStandardDeviation), MinimumStandardDeviation, "A spread of zero makes phi jump from nothing to enormous within a millisecond.");
        if (HeartbeatSampleSize < 2)
            throw new ArgumentOutOfRangeException(nameof(HeartbeatSampleSize), HeartbeatSampleSize, "Two samples are the fewest that have a spread at all.");
        if (SplitBrainStrategy == SplitBrainStrategy.StaticQuorum && StaticQuorumSize < 1)
            throw new ArgumentOutOfRangeException(nameof(StaticQuorumSize), StaticQuorumSize, "A static quorum needs a size; without one every side survives and the strategy protects nothing.");
        if (SplitBrainStabilityWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SplitBrainStabilityWindow), SplitBrainStabilityWindow, "Deciding with no settling time decides against a membership that is still moving.");
        if (GossipFanout < 0)
            throw new ArgumentOutOfRangeException(nameof(GossipFanout), GossipFanout, "Use zero for every peer, not a negative number.");
        if (JoinTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(JoinTimeout), JoinTimeout, "The startup handshake needs some time to run.");
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
