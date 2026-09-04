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
    /// Where <see cref="PersistentActor{TState}"/> keeps state. Defaults to an in-memory store,
    /// which is durable across deactivation but not across a process restart.
    /// </summary>
    public IStateStore StateStore { get; set; } = new InMemoryStateStore();

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
        if (IdleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(IdleTimeout), IdleTimeout, "IdleTimeout must be positive.");
        if (SweepInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SweepInterval), SweepInterval, "SweepInterval must be positive.");
        if (MailboxCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(MailboxCapacity), MailboxCapacity, "MailboxCapacity must be zero (unbounded) or positive.");
        if (DefaultAskTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(DefaultAskTimeout), DefaultAskTimeout, "DefaultAskTimeout must be positive.");
        Cluster.Validate();
    }
}
