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
/// <param name="MessagesDispatched">
/// Messages accepted for handling on this node - local sends and inbound remote frames alike, but
/// not messages this node forwarded to whichever peer owns their key.
/// </param>
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
    /// Messages accepted for handling on this node but not yet processed. A number that keeps
    /// climbing is the signal that the node is taking work faster than it retires it.
    /// </summary>
    /// <remarks>
    /// <see cref="MessagesDispatched"/> counts only what this node took on itself, so a message
    /// forwarded to the node that owns its key is not in flight here. Counting forwarded messages
    /// would make this climb forever on any node that routes remotely.
    /// </remarks>
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
