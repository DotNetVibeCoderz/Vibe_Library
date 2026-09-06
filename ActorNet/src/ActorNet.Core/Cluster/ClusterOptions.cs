// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Cluster;

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
