// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Metrics;

/// <summary>A point-in-time reading of one actor.</summary>
public sealed record ActorSnapshot(
    string Id,
    string Type,
    DateTimeOffset ActivatedAt,
    DateTimeOffset LastMessageAt,
    long MessagesProcessed,
    long MessagesFailed,
    int Restarts,
    int MailboxDepth,
    double AverageProcessingMicroseconds)
{
    /// <summary>How long this actor has been activated.</summary>
    public TimeSpan Age => DateTimeOffset.UtcNow - ActivatedAt;

    /// <summary>How long since it last handled anything - what the idle sweeper measures against.</summary>
    public TimeSpan Idle => DateTimeOffset.UtcNow - LastMessageAt;
}

/// <summary>A point-in-time reading of the whole node.</summary>
public sealed record ActorSystemSnapshot(
    string NodeId,
    DateTimeOffset TakenAt,
    TimeSpan Uptime,
    long MessagesDispatched,
    long MessagesProcessed,
    long MessagesFailed,
    long Activations,
    long Deactivations,
    long Restarts,
    long RemoteSent,
    long RemoteReceived,
    long AsksIssued,
    long AsksTimedOut,
    int ActiveActors,
    int MailboxDepth,
    double AverageProcessingMicroseconds,
    double AverageQueueLatencyMicroseconds,
    IReadOnlyList<ActorSnapshot> Actors)
{
    /// <summary>
    /// Messages accepted into a mailbox but not yet processed. A number that keeps climbing is the
    /// signal that the node is taking work faster than it retires it.
    /// </summary>
    public long InFlight => Math.Max(0, MessagesDispatched - MessagesProcessed - MessagesFailed);

    /// <summary>Sustained processing rate since the node started.</summary>
    public double MessagesPerSecond => Uptime.TotalSeconds <= 0 ? 0 : MessagesProcessed / Uptime.TotalSeconds;
}

/// <summary>Read side of the metrics the runtime keeps.</summary>
public interface IMetricsCollector
{
    /// <summary>Takes a consistent-enough reading of the node. Cheap enough to poll once a second.</summary>
    ActorSystemSnapshot Snapshot(bool includeActors = true);
}
