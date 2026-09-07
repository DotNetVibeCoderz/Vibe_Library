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

    /// <summary>
    /// How long an inbound remote message may wait for room in a bounded mailbox before it is
    /// refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A local sender waiting on a full mailbox is backpressure working: the thread being slowed
    /// down is the one producing the work. A remote sender cannot be slowed the same way, because
    /// the thread that would block is the connection's reader - and every other actor's traffic on
    /// that connection is queued behind it. One busy actor would stall the whole node.
    /// </para>
    /// <para>
    /// So an inbound delivery waits, but not forever. Past this deadline the message becomes a
    /// dead letter and any waiting ask is answered with <see cref="MailboxFullException"/> rather
    /// than left to time out.
    /// </para>
    /// <para>
    /// Unreachable unless <see cref="MailboxCapacity"/> is set: an unbounded mailbox always accepts.
    /// </para>
    /// </remarks>
    public TimeSpan RemoteDeliveryTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a send may wait for room in a peer's outbound queue before it is refused with
    /// <see cref="NodeCongestedException"/>.
    /// </summary>
    /// <remarks>
    /// Generous on purpose, so it fires only when a peer is genuinely stuck rather than briefly
    /// busy. Waiting indefinitely is not backpressure - it is a hang that surfaces later as an ask
    /// timeout with no indication that the peer, rather than the actor, was the problem.
    /// </remarks>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Frames that may be queued for one peer before a send has to wait for room.
    /// </summary>
    /// <remarks>
    /// The per-peer buffer that absorbs a burst without letting an unreachable or badly-behind peer
    /// grow this node's heap without limit. Raise it for bursty traffic; lower it to feel
    /// backpressure sooner. It is a count of frames, so the memory it can hold depends on how large
    /// the messages are.
    /// </remarks>
    public int OutboundQueueCapacity { get; set; } = 8192;

    /// <summary>Supervision policy for actors that were not registered with one of their own.</summary>
    public SupervisorStrategy DefaultSupervisorStrategy { get; set; } = SupervisorStrategy.Default;

    /// <summary>
    /// The encoding this node writes when it opens a connection to a peer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reading is always both: a frame says which encoding it is in, and a connection is answered
    /// in the encoding it was addressed in. So this only decides what this node sends first, and a
    /// cluster can be rolled from one to the other a node at a time without a flag day.
    /// </para>
    /// <para>
    /// The Go, Python and Node clients speak JSON only. They are unaffected - they address a node
    /// in JSON and are answered in JSON however this is set - but a client written against the
    /// binary format does not exist.
    /// </para>
    /// </remarks>
    public Network.WireFormat WireFormat { get; set; } = Network.WireFormat.Json;

    /// <summary>
    /// How many actors a departing node asks its successors to activate before it closes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rebalancing deactivates an actor and lets the next message reactivate it from the store, so
    /// after a planned restart the first message to every key that moved pays a read - all of them
    /// at once, at the moment traffic arrives. A leaving node knows which keys are moving and where
    /// to, and can say so.
    /// </para>
    /// <para>
    /// Capped because it is one message per actor and a node can hold a great many. Beyond the cap
    /// the rest activate on demand, which is the behaviour without this at all. Zero turns it off.
    /// </para>
    /// </remarks>
    public int WarmHandoffLimit { get; set; } = 1000;

    /// <summary>
    /// Most keys a node tells each successor it is holding, so an unplanned loss can be warmed too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="WarmHandoffLimit"/> only covers a node that leaves politely, because only that
    /// node knows what it was holding. When a node is lost without warning, the survivors inherit
    /// its keys and have no idea which of them were live - so every one of them pays a read at the
    /// moment traffic arrives, which is exactly when the cluster is already one node short.
    /// </para>
    /// <para>
    /// Telling each successor in advance fixes that, at the cost of a periodic message naming keys.
    /// Off by default for that reason: unlike the warm handoff, which costs nothing until a
    /// shutdown, this is traffic a healthy cluster pays all the time. Zero turns it off.
    /// </para>
    /// <para>
    /// The digest is advice, not a directory. It is a snapshot of what one node held when it last
    /// spoke, so it can name an actor that has since deactivated - warming that one costs a read
    /// and nothing else - and it can miss one activated since. It is never consulted for routing.
    /// </para>
    /// </remarks>
    public int InheritanceDigestLimit { get; set; }

    /// <summary>How often a node tells its successors what it is holding.</summary>
    /// <remarks>
    /// The cost of being wrong is one cold activation, so this is deliberately slow. It trades
    /// freshness for bandwidth, and freshness is the cheaper of the two to lose.
    /// </remarks>
    public TimeSpan InheritanceDigestInterval { get; set; } = TimeSpan.FromSeconds(15);

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
        if (InheritanceDigestLimit < 0)
            throw new ArgumentOutOfRangeException(nameof(InheritanceDigestLimit), InheritanceDigestLimit, "Use zero to turn the digest off, not a negative number.");
        if (InheritanceDigestLimit > 0 && InheritanceDigestInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(InheritanceDigestInterval), InheritanceDigestInterval, "An interval of zero publishes the digest in a tight loop.");
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
        if (RemoteDeliveryTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RemoteDeliveryTimeout), RemoteDeliveryTimeout, "RemoteDeliveryTimeout must be positive.");
        if (SendTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SendTimeout), SendTimeout, "SendTimeout must be positive.");
        if (OutboundQueueCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(OutboundQueueCapacity), OutboundQueueCapacity, "OutboundQueueCapacity must be positive.");
        Cluster.Validate();
        Security.Validate();
    }

    /// <summary>Addresses that mean "every interface" to a listener and nothing at all to a dialler.</summary>
    private static bool IsUnroutable(string host) =>
        string.IsNullOrWhiteSpace(host) || host is "0.0.0.0" or "::" or "[::]" or "*";
}
