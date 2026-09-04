// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using System.Diagnostics;

namespace ActorNet.Metrics;

/// <summary>
/// The runtime's counters. Every write is a single interlocked operation on the hot path; nothing
/// here takes a lock, and nothing allocates until <see cref="Snapshot"/> is called.
/// </summary>
/// <remarks>
/// A snapshot is not a transactional view - counters are read one after another while the node
/// keeps running, so <see cref="ActorSystemSnapshot.InFlight"/> can read slightly negative in
/// principle and is clamped. That is the right trade: an exact reading would mean stopping the
/// world for a dashboard poll.
/// </remarks>
public sealed class MetricsCollector : IMetricsCollector
{
    private static readonly double TicksToMicroseconds = 1_000_000.0 / Stopwatch.Frequency;

    private readonly ConcurrentDictionary<ActorId, ActorMetrics> _actors = new();
    private readonly long _startedTimestamp = Stopwatch.GetTimestamp();
    private readonly string _nodeId;

    private long _messagesDispatched;
    private long _messagesProcessed;
    private long _messagesFailed;
    private long _activations;
    private long _deactivations;
    private long _restarts;
    private long _remoteSent;
    private long _remoteReceived;
    private long _asksIssued;
    private long _asksTimedOut;
    private long _processingTicks;
    private long _queueLatencyTicks;

    public MetricsCollector(string nodeId) => _nodeId = nodeId;

    /// <summary>Registers an actor so it shows up in snapshots. Called once per activation.</summary>
    public ActorMetrics RegisterActor(ActorId id, string typeName, Func<int> mailboxDepth)
    {
        Interlocked.Increment(ref _activations);
        var metrics = new ActorMetrics(typeName, mailboxDepth);
        _actors[id] = metrics;
        return metrics;
    }

    /// <summary>Removes an actor from snapshots. Called once per deactivation.</summary>
    public void UnregisterActor(ActorId id)
    {
        if (_actors.TryRemove(id, out _)) Interlocked.Increment(ref _deactivations);
    }

    public void RecordDispatched() => Interlocked.Increment(ref _messagesDispatched);

    /// <summary>Records a successfully handled message, folding its timings into both the actor and the node.</summary>
    public void RecordProcessed(ActorMetrics actor, long queueLatencyTicks, long processingTicks)
    {
        Interlocked.Increment(ref _messagesProcessed);
        Interlocked.Add(ref _processingTicks, processingTicks);
        Interlocked.Add(ref _queueLatencyTicks, queueLatencyTicks);
        actor.RecordProcessed(processingTicks);
    }

    public void RecordFailed(ActorMetrics? actor)
    {
        Interlocked.Increment(ref _messagesFailed);
        actor?.RecordFailed();
    }

    public void RecordRestart(ActorMetrics? actor)
    {
        Interlocked.Increment(ref _restarts);
        actor?.RecordRestart();
    }

    public void RecordRemoteSent() => Interlocked.Increment(ref _remoteSent);
    public void RecordRemoteReceived() => Interlocked.Increment(ref _remoteReceived);
    public void RecordAskIssued() => Interlocked.Increment(ref _asksIssued);
    public void RecordAskTimedOut() => Interlocked.Increment(ref _asksTimedOut);

    /// <inheritdoc />
    public ActorSystemSnapshot Snapshot(bool includeActors = true)
    {
        var processed = Interlocked.Read(ref _messagesProcessed);
        var processingTicks = Interlocked.Read(ref _processingTicks);
        var queueTicks = Interlocked.Read(ref _queueLatencyTicks);

        var actors = includeActors
            ? _actors.Select(pair => pair.Value.ToSnapshot(pair.Key)).OrderBy(a => a.Id, StringComparer.Ordinal).ToArray()
            : [];

        var depth = 0;
        foreach (var actor in _actors.Values) depth += actor.MailboxDepth;

        return new ActorSystemSnapshot(
            NodeId: _nodeId,
            TakenAt: DateTimeOffset.UtcNow,
            Uptime: Stopwatch.GetElapsedTime(_startedTimestamp),
            MessagesDispatched: Interlocked.Read(ref _messagesDispatched),
            MessagesProcessed: processed,
            MessagesFailed: Interlocked.Read(ref _messagesFailed),
            Activations: Interlocked.Read(ref _activations),
            Deactivations: Interlocked.Read(ref _deactivations),
            Restarts: Interlocked.Read(ref _restarts),
            RemoteSent: Interlocked.Read(ref _remoteSent),
            RemoteReceived: Interlocked.Read(ref _remoteReceived),
            AsksIssued: Interlocked.Read(ref _asksIssued),
            AsksTimedOut: Interlocked.Read(ref _asksTimedOut),
            ActiveActors: _actors.Count,
            MailboxDepth: depth,
            AverageProcessingMicroseconds: processed == 0 ? 0 : processingTicks * TicksToMicroseconds / processed,
            AverageQueueLatencyMicroseconds: processed == 0 ? 0 : queueTicks * TicksToMicroseconds / processed,
            Actors: actors);
    }

    /// <summary>Per-actor counters. One instance per activation, handed to the actor's cell.</summary>
    public sealed class ActorMetrics(string typeName, Func<int> mailboxDepth)
    {
        private readonly long _activatedTicks = DateTimeOffset.UtcNow.UtcTicks;
        private long _lastMessageTicks = DateTimeOffset.UtcNow.UtcTicks;
        private long _processed;
        private long _failed;
        private long _processingTicks;
        private int _restarts;

        internal int MailboxDepth
        {
            get
            {
                // The depth probe reads a channel that may already be completed during shutdown.
                try { return mailboxDepth(); }
                catch (ObjectDisposedException) { return 0; }
            }
        }

        internal void RecordProcessed(long processingTicks)
        {
            Interlocked.Increment(ref _processed);
            Interlocked.Add(ref _processingTicks, processingTicks);
            Interlocked.Exchange(ref _lastMessageTicks, DateTimeOffset.UtcNow.UtcTicks);
        }

        internal void RecordFailed()
        {
            Interlocked.Increment(ref _failed);
            Interlocked.Exchange(ref _lastMessageTicks, DateTimeOffset.UtcNow.UtcTicks);
        }

        internal void RecordRestart() => Interlocked.Increment(ref _restarts);

        internal ActorSnapshot ToSnapshot(ActorId id)
        {
            var processed = Interlocked.Read(ref _processed);
            return new ActorSnapshot(
                Id: id.ToString(),
                Type: typeName,
                ActivatedAt: new DateTimeOffset(_activatedTicks, TimeSpan.Zero),
                LastMessageAt: new DateTimeOffset(Interlocked.Read(ref _lastMessageTicks), TimeSpan.Zero),
                MessagesProcessed: processed,
                MessagesFailed: Interlocked.Read(ref _failed),
                Restarts: Volatile.Read(ref _restarts),
                MailboxDepth: MailboxDepth,
                AverageProcessingMicroseconds: processed == 0 ? 0 : Interlocked.Read(ref _processingTicks) * TicksToMicroseconds / processed);
        }
    }
}
