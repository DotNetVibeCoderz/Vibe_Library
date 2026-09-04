// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Cluster;
using ActorNet.Persistence;

namespace ActorNet;

/// <summary>Everything that shapes a node's behaviour, in one place.</summary>
public sealed class ActorSystemOptions
{
    /// <summary>
    /// This node's identity. Must be unique and stable within a cluster - it is what the
    /// consistent-hash ring places keys against, so a node that comes back with a different id is
    /// a different node and takes a different slice of the keyspace.
    /// </summary>
    public string NodeId { get; set; } = $"node-{Environment.MachineName.ToLowerInvariant()}-{Environment.ProcessId}";

    /// <summary>Address the transport binds to. Empty disables the listener entirely (single-process mode).</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Port the transport binds to. Zero picks a free port, which the system reports back after start.</summary>
    public int Port { get; set; }

    /// <summary>
    /// The host peers are told to dial, when that is not the host this node binds to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Binding and advertising answer different questions. <see cref="Host"/> is "which interfaces
    /// do I accept connections on", and the widest useful answer is <c>0.0.0.0</c>. This is "what
    /// should a peer type to reach me", and <c>0.0.0.0</c> is a meaningless answer to that - a peer
    /// given it will dial nothing, and eventually mark a healthy node unreachable.
    /// </para>
    /// <para>
    /// Leave it null and the bind host is advertised, which is right whenever a node binds to an
    /// address peers can already route to. Set it when they differ: a container binding all
    /// interfaces but reachable by service name, or a host behind NAT.
    /// </para>
    /// </remarks>
    public string? AdvertisedHost { get; set; }

    /// <summary>
    /// The port peers are told to dial, when that is not the port this node binds to.
    /// </summary>
    /// <remarks>
    /// The case this exists for is a published container port: bind 9000 inside, publish it as
    /// 19000 outside, and advertise 19000. Leave it null and the bound port is advertised - which
    /// is also what resolves <see cref="Port"/> being zero, since the real port is only known after
    /// the listener starts.
    /// </remarks>
    public int? AdvertisedPort { get; set; }

    /// <summary>The host a peer should dial to reach this node.</summary>
    public string EffectiveAdvertisedHost => string.IsNullOrWhiteSpace(AdvertisedHost) ? Host : AdvertisedHost;

    /// <summary>False leaves the node completely in-process: no socket is opened.</summary>
    public bool EnableNetworking { get; set; } = true;

    /// <summary>
    /// How long an actor may sit without messages before the sweeper deactivates it. This is the
    /// "virtual" in virtual actor: nothing is destroyed, and the next message re-activates from
    /// persisted state.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often the idle sweeper runs. Also the granularity of <see cref="IdleTimeout"/>.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Mailbox capacity per actor. Zero means unbounded; a positive value applies backpressure,
    /// so a sender awaiting a tell blocks once the target is that far behind.
    /// </summary>
    /// <remarks>
    /// Unbounded is the default because it is what makes a tell cheap, but it converts a slow
    /// consumer into unbounded memory growth. Set this on any actor fed by an external firehose.
    /// </remarks>
    public int MailboxCapacity { get; set; }

    /// <summary>Timeout applied to an ask that does not specify one.</summary>
    public TimeSpan DefaultAskTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Supervision policy for actors that were not registered with one of their own.</summary>
    public SupervisorStrategy DefaultSupervisorStrategy { get; set; } = SupervisorStrategy.Default;

    /// <summary>Cluster membership and placement settings.</summary>
    public ClusterOptions Cluster { get; set; } = new();

    /// <summary>
    /// Encryption and authentication between nodes. Both off by default.
    /// </summary>
    /// <remarks>
    /// Off is the honest default for a library whose first run is on a laptop, and the reason the
    /// documentation says to keep a cluster on a trusted network until they are on.
    /// </remarks>
    public Network.ClusterSecurityOptions Security { get; set; } = new();

    /// <summary>
    /// Where <see cref="PersistentActor{TState}"/> keeps state. Defaults to an in-memory store,
    /// which is durable across deactivation but not across a process restart.
    /// </summary>
    public IStateStore StateStore { get; set; } = new InMemoryStateStore();

    /// <summary>
    /// Where undeliverable messages are recorded.
    /// </summary>
    /// <remarks>
    /// Bounded and dropping the oldest by default. A node that is failing to deliver usually fails
    /// a lot, and an unbounded record of that is a second outage on top of the first.
    /// </remarks>
    public Runtime.IDeadLetterQueue DeadLetters { get; set; } = new Runtime.DeadLetterQueue();

    /// <summary>Where <see cref="EventSourcedActor{TState}"/> appends events.</summary>
    public IEventJournal EventJournal { get; set; } = new InMemoryEventJournal();

    /// <summary>Where event-sourced actors keep snapshots, so recovery does not replay from zero.</summary>
    public ISnapshotStore SnapshotStore { get; set; } = new InMemorySnapshotStore();

    /// <summary>Throws when the options cannot produce a working node.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(NodeId))
            throw new ArgumentException("NodeId must be set; it is the cluster's identity for this process.", nameof(NodeId));
        if (Port is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be between 0 and 65535.");
        if (AdvertisedPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(AdvertisedPort), AdvertisedPort,
                "AdvertisedPort must be between 1 and 65535. Leave it null to advertise the bound port.");

        // Only meaningful in a cluster - a standalone node advertises to nobody. Checked here
        // rather than left to fail later, because the symptom is a peer that keeps marking a
        // perfectly healthy node unreachable, which looks like a network problem and is not one.
        if (Cluster.Enabled && IsUnroutable(EffectiveAdvertisedHost))
            throw new ArgumentException(
                $"'{EffectiveAdvertisedHost}' is a bind address, not an address a peer can dial. " +
                "It is fine for Host - it means \"accept on every interface\" - but a peer told to dial it reaches nothing. " +
                "Set AdvertisedHost to an address or hostname peers can route to.",
                nameof(AdvertisedHost));
        if (IdleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(IdleTimeout), IdleTimeout, "IdleTimeout must be positive.");
        if (SweepInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SweepInterval), SweepInterval, "SweepInterval must be positive.");
        if (MailboxCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(MailboxCapacity), MailboxCapacity, "MailboxCapacity must be zero (unbounded) or positive.");
        if (DefaultAskTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(DefaultAskTimeout), DefaultAskTimeout, "DefaultAskTimeout must be positive.");
        Cluster.Validate();
        Security.Validate();
    }

    /// <summary>Addresses that mean "every interface" to a listener and nothing at all to a dialler.</summary>
    private static bool IsUnroutable(string host) =>
        string.IsNullOrWhiteSpace(host) || host is "0.0.0.0" or "::" or "[::]" or "*";
}
