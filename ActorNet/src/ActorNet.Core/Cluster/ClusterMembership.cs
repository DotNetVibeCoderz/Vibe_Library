// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using ActorNet.Network;
using ActorNet.Serialization;
using Microsoft.Extensions.Logging;

namespace ActorNet.Cluster;

/// <summary>
/// Membership, failure detection and key placement for one node.
/// </summary>
/// <remarks>
/// <para>
/// The protocol is deliberately small: a joiner sends <see cref="WireKind.Join"/> to its seeds and
/// gets back the seed's member table; from then on every node periodically sends its whole table
/// to <see cref="ClusterOptions.GossipFanout"/> of its peers, taken in rotation, and the rest of
/// the cluster hears it second-hand. That is gossip in the loose sense - it converges in about
/// log(members) rounds at a cost of O(members x fanout) frames per interval.
/// </para>
/// <para>
/// Failure detection is phi-accrual by default: suspicion is measured against how long a peer's
/// beats have actually been taking, so the same threshold is patient with a slow link and quick
/// with a fast one. Either way a suspected peer is only <see cref="MemberStatus.Unreachable"/> and
/// stays <em>on</em> the ring; it takes the higher threshold to take it off, because the usual
/// cause of silence is a pause rather than a departure.
/// </para>
/// <para>
/// A node's own entry is never overwritten by what a peer thinks of it. If a peer reports us
/// unreachable, we bump our incarnation, which makes our own view win everywhere it spreads. That
/// is the one piece of SWIM worth having without the rest of it.
/// </para>
/// </remarks>
public sealed class ClusterMembership : IClusterView, IAsyncDisposable
{
    private readonly ClusterOptions _options;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, ClusterMember> _members = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _ringGate = new();

    private readonly PhiAccrualFailureDetector _detector;

    private int _gossipCursor;
    private readonly HashSet<string> _departed = new(StringComparer.Ordinal);
    private string[] _lastAgreedMembership = [];
    private string _partitionSignature = string.Empty;
    private DateTimeOffset _partitionSince;
    private bool _selfDowned;

    private ITransport? _transport;
    private Task? _heartbeatLoop;
    private HashRing _ring;
    private long _incarnation = 1;

    /// <inheritdoc />
    public string SelfNodeId { get; }

    /// <inheritdoc />
    public event Action<IReadOnlyList<ClusterMember>>? MembershipChanged;

    /// <summary>
    /// Raised when this node has decided it is on the losing side of a partition.
    /// </summary>
    /// <remarks>
    /// The node has already taken itself off the ring by the time this fires. The host is expected
    /// to stop: a node that keeps serving actors it no longer owns is the thing the whole strategy
    /// exists to prevent. Rejoining means starting again.
    /// </remarks>
    public event Action<string>? SelfDowned;

    /// <param name="advertisedHost">
    /// What peers are told to dial - not necessarily what the listener binds to. A node binding
    /// every interface still has to advertise one address a peer can actually reach.
    /// </param>
    /// <param name="advertisedPort">
    /// The port peers dial. Superseded by <see cref="SetAdvertisedPort"/> once the listener has
    /// bound, unless the caller pinned one.
    /// </param>
    public ClusterMembership(string nodeId, string advertisedHost, int advertisedPort, ClusterOptions options, ILogger logger)
    {
        SelfNodeId = nodeId;
        _options = options;
        _logger = logger;
        _members[nodeId] = new ClusterMember(nodeId, advertisedHost, advertisedPort, MemberStatus.Up, DateTimeOffset.UtcNow, _incarnation);
        _detector = new PhiAccrualFailureDetector(
            options.HeartbeatInterval,
            options.AcceptableHeartbeatPause,
            options.MinimumStandardDeviation,
            options.HeartbeatSampleSize);
        _ring = BuildRing();
    }

    /// <inheritdoc />
    public HashRing Ring => Volatile.Read(ref _ring);

    /// <inheritdoc />
    public bool IsSingleNode => !_options.Enabled || _members.Count(m => m.Value.IsRoutable) <= 1;

    /// <inheritdoc />
    public IReadOnlyList<ClusterMember> Members => _members.Values.OrderBy(m => m.NodeId, StringComparer.Ordinal).ToArray();

    /// <inheritdoc />
    public string OwnerOf(ActorId id)
    {
        // Clustering off, or nobody else here: everything is ours. Skipping the ring in that case
        // is not just an optimization, it is what lets a standalone node work with an empty one.
        if (IsSingleNode) return SelfNodeId;

        var ring = Volatile.Read(ref _ring);
        return ring.IsEmpty ? SelfNodeId : ring.OwnerOf(id.ToString());
    }

    /// <inheritdoc />
    public bool IsLocal(ActorId id) => string.Equals(OwnerOf(id), SelfNodeId, StringComparison.Ordinal);

    /// <summary>
    /// Sets the port peers are told to dial, once the transport has bound one.
    /// </summary>
    /// <remarks>
    /// This is what resolves a configured port of zero: the real port is only known after the
    /// listener starts. A caller that pinned an advertised port passes that instead, which is the
    /// published-port case.
    /// </remarks>
    public void SetAdvertisedPort(int port)
    {
        _members.AddOrUpdate(SelfNodeId,
            _ => throw new InvalidOperationException("Self member is missing."),
            (_, existing) => existing with { Port = port });
    }

    /// <summary>Starts membership: contacts the seeds, then beats on a timer.</summary>
    public async Task StartAsync(ITransport transport, CancellationToken cancellationToken)
    {
        _transport = transport;
        if (!_options.Enabled)
        {
            _logger.LogInformation("Clustering is disabled; running as a standalone node.");
            return;
        }

        // Starting is not allowed to wait on a silent seed. Coming up alone is a working state -
        // the handshake is retried on every beat - whereas a node that has not finished starting
        // cannot even serve the actors it already owns.
        // A seed that runs out of time is reported the same way as one that refused: the handshake
        // counts how many answered, and "Join sent to 0 of 2 seeds" is the line either way.
        using (var joining = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            joining.CancelAfter(_options.JoinTimeout);
            await JoinSeedsAsync(joining.Token).ConfigureAwait(false);
        }

        _heartbeatLoop = Task.Run(() => HeartbeatLoopAsync(_cts.Token), CancellationToken.None);
    }

    /// <summary>Announces departure so peers take this node off the ring without waiting for a timeout.</summary>
    public async Task LeaveAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || _transport is null) return;

        var frame = new WireEnvelope { Kind = WireKind.Leave, FromNode = SelfNodeId };
        foreach (var member in _members.Values.Where(m => m.NodeId != SelfNodeId))
        {
            try { await _transport.SendAsync(member.NodeId, frame, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not tell {NodeId} that this node is leaving.", member.NodeId); }
        }
    }

    /// <summary>
    /// Runs the seed handshake again while this node knows no routable peer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handshake used to be attempted exactly once, at startup. A node whose seeds were not
    /// reachable in that instant stayed alone forever, even after they came up seconds later - and
    /// "not reachable in that instant" is the normal case, not the exceptional one: nodes start in
    /// arbitrary order, and a single dropped packet on a wireless link is enough.
    /// </para>
    /// <para>
    /// Found by running two nodes on two machines. Every test until then started the seed first,
    /// on loopback, where a connect does not transiently fail - so the suite could not have caught
    /// it.
    /// </para>
    /// <para>
    /// This also covers recovery: a node whose only peers have all gone <see cref="MemberStatus.Down"/>
    /// starts seeking them out again rather than waiting for someone else to make the first move.
    /// </para>
    /// </remarks>
    private async Task RejoinIfAloneAsync(CancellationToken cancellationToken)
    {
        if (_options.Seeds.Count == 0) return;

        // A peer that is merely unreachable still counts - it is on the ring and expected back, and
        // re-seeding on a blip would be churn rather than recovery.
        foreach (var (nodeId, member) in _members)
        {
            if (nodeId != SelfNodeId && member.IsRoutable) return;
        }

        await JoinSeedsAsync(cancellationToken, isRetry: true).ConfigureAwait(false);
    }

    private async Task JoinSeedsAsync(CancellationToken cancellationToken, bool isRetry = false)
    {
        var self = _members[SelfNodeId];
        var frame = new WireEnvelope
        {
            Kind = WireKind.Join,
            FromNode = SelfNodeId,
            Members = [ToWire(self)],
        };

        // Seeds are contacted at once rather than in turn. They are independent messages, and one
        // seed behind a firewall that drops instead of refusing takes the OS a minute to give up
        // on - long enough, in turn, to starve every seed listed after it. Listing a second seed
        // is meant to make a join more likely, not to make it hostage to the order.
        var attempts = new List<Task<bool>>(_options.Seeds.Count);
        foreach (var seed in _options.Seeds)
        {
            if (!ClusterOptions.TryParseSeed(seed, out var host, out var port)) continue;
            if (port == self.Port && IsSelfHost(host)) continue;

            attempts.Add(SendJoinAsync(seed, host, port, frame, cancellationToken));
        }

        var reached = 0;
        foreach (var answered in await Task.WhenAll(attempts).ConfigureAwait(false))
        {
            if (answered) reached++;
        }

        // Retries run on every heartbeat while the node is alone, so logging each one at
        // Information would bury everything else. The first attempt still says what happened.
        if (isRetry)
            _logger.LogDebug("Still alone; reached {Reached} of {Total} seeds.", reached, _options.Seeds.Count);
        else
            _logger.LogInformation("Join sent to {Reached} of {Total} seeds.", reached, _options.Seeds.Count);
    }

    /// <summary>Sends one join frame. Never throws: a seed that is down is an ordinary outcome.</summary>
    private async Task<bool> SendJoinAsync(string seed, string host, int port, WireEnvelope frame, CancellationToken cancellationToken)
    {
        try
        {
            await _transport!.SendToAddressAsync(host, port, frame, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            // A seed that is down is normal - the first node to start has none reachable.
            _logger.LogDebug(ex, "Seed {Seed} did not answer the join.", seed);
            return false;
        }
    }

    private bool IsSelfHost(string host) =>
        string.Equals(host, _members[SelfNodeId].Host, StringComparison.OrdinalIgnoreCase) ||
        host is "127.0.0.1" or "localhost" or "0.0.0.0";

    /// <summary>The address this node advertises, for diagnostics and the console.</summary>
    public string AdvertisedAddress => _members[SelfNodeId].Address;

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.HeartbeatInterval);
        while (await SafeWaitAsync(timer, cancellationToken).ConfigureAwait(false))
        {
            // A peer whose packets are silently dropped - a firewall that discards rather than
            // refuses is the common case - takes the OS a minute or more to give up on. Awaiting
            // that here would hold up failure detection and every other peer's beat behind one
            // black hole, so a round gets one interval and then moves on.
            using var round = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            round.CancelAfter(_options.HeartbeatInterval);

            try
            {
                DetectFailures();
                await RejoinIfAloneAsync(round.Token).ConfigureAwait(false);
                await GossipAsync(round.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (round.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Heartbeat round ran out of time; the next one starts on schedule.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Heartbeat round failed.");
            }
        }
    }

    private static async ValueTask<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try { return await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return false; }
    }

    /// <summary>Runs one gossip beat. Internal so a test can drive the rotation a beat at a time.</summary>
    internal async Task GossipAsync(CancellationToken cancellationToken)
    {
        var frame = new WireEnvelope
        {
            Kind = WireKind.Gossip,
            FromNode = SelfNodeId,
            Members = _members.Values.Select(ToWire).ToList(),
        };

        if (_selfDowned) return;

        foreach (var member in NextGossipTargets())
        {
            try
            {
                await _transport!.SendAsync(member.NodeId, frame, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Gossip to {NodeId} failed.", member.NodeId);
            }
        }
    }

    /// <summary>
    /// The peers this beat gossips to, taken in rotation.
    /// </summary>
    /// <remarks>
    /// Sorted by node id so every node walks the same list in the same order, and advanced by a
    /// cursor so consecutive beats cover different peers. The rotation is what makes the gap
    /// between any two nodes a fixed number of beats rather than a matter of luck - the failure
    /// detector can learn a fixed gap and cannot learn a coin flip.
    /// </remarks>
    private List<ClusterMember> NextGossipTargets()
    {
        var peers = _members.Values
            .Where(m => m.NodeId != SelfNodeId && m.Status != MemberStatus.Down)
            .OrderBy(m => m.NodeId, StringComparer.Ordinal)
            .ToList();

        var fanout = _options.GossipFanout;
        if (fanout <= 0 || fanout >= peers.Count) return peers;

        var start = _gossipCursor;
        _gossipCursor = (start + fanout) % peers.Count;

        // Wraps, so the peers at the end of the list are not perpetually the ones left over when
        // the count does not divide evenly.
        return Enumerable.Range(0, fanout).Select(i => peers[(start + i) % peers.Count]).ToList();
    }

    /// <summary>
    /// Tells the detector how often a peer's beats are actually due, given the fanout.
    /// </summary>
    /// <remarks>
    /// With a fanout, a peer is heard from every <c>ceil(peers / fanout)</c> beats rather than
    /// every beat. A detector still expecting one beat per interval would suspect every newly
    /// discovered member in a large cluster before its window had filled with the truth.
    /// </remarks>
    private void UpdateExpectedHeartbeatInterval()
    {
        var peers = _members.Values.Count(m => m.NodeId != SelfNodeId && m.Status != MemberStatus.Down);
        var fanout = _options.GossipFanout;
        var rounds = fanout <= 0 || peers <= fanout ? 1 : (int)Math.Ceiling((double)peers / fanout);

        _detector.FirstHeartbeatEstimate = _options.HeartbeatInterval * rounds;
    }

    /// <summary>What this node now believes about a peer, given how long it has been quiet.</summary>
    private MemberStatus Judge(string nodeId, ClusterMember member, DateTimeOffset now)
    {
        // A member that has gone down stays down until it makes contact - which revives it in
        // RecordContact. Re-judging it here would only ever confirm what is already true, and a
        // forgotten history would make its phi zero and quietly resurrect it.
        if (member.Status == MemberStatus.Down) return MemberStatus.Down;

        var (suspect, condemn) = _options.FailureDetection == FailureDetection.PhiAccrual
            ? Phi(nodeId, now)
            : Deadline(member, now);

        return condemn ? MemberStatus.Down
            : suspect ? MemberStatus.Unreachable
            : member.Status == MemberStatus.Unreachable ? MemberStatus.Up
            : member.Status;

        (bool Suspect, bool Condemn) Phi(string id, DateTimeOffset at)
        {
            var phi = _detector.Phi(id, at);
            return (phi >= _options.PhiUnreachableThreshold, phi >= _options.PhiDownThreshold);
        }

        (bool Suspect, bool Condemn) Deadline(ClusterMember peer, DateTimeOffset at)
        {
            var silence = at - peer.LastSeen;
            return (silence > _options.UnreachableAfter, silence > _options.DownAfter);
        }
    }

    private void DetectFailures()
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;

        foreach (var (id, member) in _members)
        {
            if (id == SelfNodeId) continue;

            var silence = now - member.LastSeen;
            var status = Judge(id, member, now);

            if (status == member.Status) continue;

            // A node coming back off the ring starts from a clean estimate. Keeping its history
            // would fold the whole outage into the window as one enormous interval, and the peer
            // would then be trusted through silences it ought to be suspected for.
            if (status == MemberStatus.Down) _detector.Forget(id);

            _members[id] = member with { Status = status };
            changed = true;
            _logger.LogWarning("Member {NodeId} is now {Status} after {Silence:N1}s of silence.", id, status, silence.TotalSeconds);
        }

        if (changed) RebuildRing();

        EvaluatePartition(now);
    }

    /// <summary>
    /// Decides whether this node is on the side of a partition that should keep serving.
    /// </summary>
    /// <remarks>
    /// Runs after failure detection, so it judges statuses this beat has already settled. It acts
    /// only once the reachable set has held still for
    /// <see cref="ClusterOptions.SplitBrainStabilityWindow"/>: a partition is rarely a clean cut,
    /// and deciding on the first observation means deciding against a membership still in motion.
    /// </remarks>
    private void EvaluatePartition(DateTimeOffset now)
    {
        if (_options.SplitBrainStrategy == SplitBrainStrategy.None || _selfDowned) return;

        var considered = _members.Values.Where(m => !_departed.Contains(m.NodeId)).ToArray();

        var reachable = considered
            .Where(m => m.Status == MemberStatus.Up)
            .Select(m => m.NodeId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        // Down counts, not just Unreachable. A peer that has been silent long enough to be taken
        // off the ring is the clearest evidence of a partition there is, and treating only the
        // in-between state as evidence would mean the window expiring before the decision.
        if (reachable.Length == considered.Length)
        {
            // Everything in sight is healthy, so this is the membership to measure a future
            // partition against. Measuring against what a side can currently see would let each
            // side call itself a majority of itself.
            _lastAgreedMembership = reachable;
            _partitionSignature = string.Empty;
            return;
        }

        var signature = string.Join(",", reachable);
        if (signature != _partitionSignature)
        {
            _partitionSignature = signature;
            _partitionSince = now;
            return;
        }

        if (now - _partitionSince < _options.SplitBrainStabilityWindow) return;

        var survives = _options.SplitBrainStrategy switch
        {
            SplitBrainStrategy.KeepMajority => SurvivesMajority(_lastAgreedMembership, reachable),
            SplitBrainStrategy.StaticQuorum => reachable.Length >= _options.StaticQuorumSize,
            _ => true,
        };

        if (survives)
        {
            // Said once per partition, not once per beat: the signature only changes when the
            // reachable set does.
            _partitionSignature = signature + "|decided";
            _logger.LogWarning(
                "Partitioned: {Reachable} of {Total} members reachable. This side keeps serving.",
                reachable.Length, _lastAgreedMembership.Length);
            return;
        }

        DownSelf($"{reachable.Length} of {_lastAgreedMembership.Length} members reachable under {_options.SplitBrainStrategy}");
    }

    /// <summary>
    /// Whether a side holding <paramref name="reachable"/> outvotes the membership everyone last
    /// agreed on.
    /// </summary>
    /// <remarks>
    /// An exact half survives only if it holds the lowest node id. Without that tiebreak a two-node
    /// cluster would lose both halves to a single broken link, which is a worse outcome than the
    /// split brain being guarded against.
    /// </remarks>
    internal static bool SurvivesMajority(IReadOnlyCollection<string> agreed, IReadOnlyCollection<string> reachable)
    {
        if (agreed.Count == 0) return true;

        // Members that joined after the split do not get a vote on it. Counting them would let a
        // side manufacture a majority by starting nodes.
        var side = reachable.Where(id => agreed.Contains(id, StringComparer.Ordinal)).ToArray();

        if (side.Length * 2 > agreed.Count) return true;
        if (side.Length * 2 < agreed.Count) return false;

        var lowest = agreed.Min(StringComparer.Ordinal)!;
        return side.Contains(lowest, StringComparer.Ordinal);
    }

    /// <summary>Takes this node off the ring and tells the host, which is expected to stop.</summary>
    private void DownSelf(string because)
    {
        _selfDowned = true;
        _members[SelfNodeId] = _members[SelfNodeId] with { Status = MemberStatus.Down };

        _logger.LogCritical("Split brain: {Because}. Taking this node down; restart it to rejoin.", because);
        RebuildRing();

        var handler = SelfDowned;
        if (handler is null) return;

        // Off the heartbeat thread: the handler is expected to stop the node, and stopping waits on
        // actors that are still draining.
        _ = Task.Run(() =>
        {
            try { handler(because); }
            catch (Exception ex) { _logger.LogError(ex, "A split-brain subscriber threw."); }
        });
    }

    /// <summary>Handles a membership frame. Returns a reply frame when the protocol calls for one.</summary>
    public WireEnvelope? HandleFrame(WireEnvelope frame)
    {
        if (frame.FromNode is { Length: > 0 } from) RecordContact(from);

        switch (frame.Kind)
        {
            case WireKind.Join:
                MergeMembers(frame.Members);
                return new WireEnvelope
                {
                    Kind = WireKind.JoinAck,
                    FromNode = SelfNodeId,
                    Members = _members.Values.Select(ToWire).ToList(),
                };

            case WireKind.JoinAck:
            case WireKind.Gossip:
                MergeMembers(frame.Members);
                return null;

            case WireKind.Leave:
                if (frame.FromNode is { Length: > 0 } leaving && _members.TryGetValue(leaving, out var member))
                {
                    // Remembered as a departure rather than a failure. Split-brain resolution counts
                    // members that stopped answering; one that said goodbye is not a missing half of
                    // the cluster, and counting it as one would leave this node permanently
                    // convinced it was partitioned.
                    _departed.Add(leaving);
                    _members[leaving] = member with { Status = MemberStatus.Down, Incarnation = member.Incarnation + 1 };
                    _logger.LogInformation("Member {NodeId} left gracefully.", leaving);
                    RebuildRing();
                }

                return null;

            default:
                return null;
        }
    }

    /// <summary>Notes that a node is alive, because a frame just arrived from it.</summary>
    public void RecordContact(string nodeId)
    {
        if (nodeId == SelfNodeId) return;

        if (_members.TryGetValue(nodeId, out var member))
        {
            var now = DateTimeOffset.UtcNow;
            _detector.Heartbeat(nodeId, now);

            var revived = member.Status is MemberStatus.Unreachable or MemberStatus.Down;
            _members[nodeId] = member with { LastSeen = now, Status = revived ? MemberStatus.Up : member.Status };
            if (revived)
            {
                _logger.LogInformation("Member {NodeId} is reachable again.", nodeId);
                RebuildRing();
            }
        }
    }

    private void MergeMembers(List<WireMember>? incoming)
    {
        if (incoming is null || incoming.Count == 0) return;

        var changed = false;
        var now = DateTimeOffset.UtcNow;

        foreach (var wire in incoming)
        {
            if (string.IsNullOrEmpty(wire.NodeId)) continue;

            if (wire.NodeId == SelfNodeId)
            {
                // Someone else's opinion of us. Refute anything but "up" by out-incarnating it.
                if ((MemberStatus)wire.Status is MemberStatus.Unreachable or MemberStatus.Down &&
                    wire.Incarnation >= Interlocked.Read(ref _incarnation))
                {
                    var bumped = Interlocked.Increment(ref _incarnation);
                    _members[SelfNodeId] = _members[SelfNodeId] with { Status = MemberStatus.Up, Incarnation = bumped, LastSeen = now };
                    _logger.LogInformation("A peer reported this node as {Status}; refuting at incarnation {Incarnation}.", (MemberStatus)wire.Status, bumped);
                    changed = true;
                }

                continue;
            }

            if (_members.TryGetValue(wire.NodeId, out var known))
            {
                // Third-party news only wins with a strictly newer incarnation. Otherwise this
                // node's own observations - which include first-hand contact - stand.
                if (wire.Incarnation <= known.Incarnation) continue;

                _members[wire.NodeId] = known with
                {
                    Host = wire.Host,
                    Port = wire.Port,
                    Status = (MemberStatus)wire.Status,
                    Incarnation = wire.Incarnation,
                };
                changed = true;
            }
            else
            {
                _members[wire.NodeId] = new ClusterMember(wire.NodeId, wire.Host, wire.Port, (MemberStatus)wire.Status, now, wire.Incarnation);

                // Start the clock on a member this node has only been told about. Without this its
                // phi would stay at zero - never heard from, therefore never suspected.
                _detector.Heartbeat(wire.NodeId, now);
                _logger.LogInformation("Discovered member {NodeId} at {Host}:{Port}.", wire.NodeId, wire.Host, wire.Port);
                changed = true;
            }
        }

        if (changed) RebuildRing();
    }

    private HashRing BuildRing() =>
        new(_members.Values.Where(m => m.IsRoutable).Select(m => m.NodeId), _options.VirtualNodesPerMember);

    private void RebuildRing()
    {
        HashRing ring;
        lock (_ringGate)
        {
            ring = BuildRing();
            Volatile.Write(ref _ring, ring);
        }

        UpdateExpectedHeartbeatInterval();

        var snapshot = Members;
        _logger.LogInformation("Ring rebuilt over {Count} routable member(s): {Members}.",
            ring.Nodes.Count, string.Join(", ", ring.Nodes));

        // Off the caller's thread: subscribers rebalance actors, and that must not run inside a
        // gossip frame handler.
        _ = Task.Run(() =>
        {
            try { MembershipChanged?.Invoke(snapshot); }
            catch (Exception ex) { _logger.LogError(ex, "A membership subscriber threw."); }
        });
    }

    /// <summary>Address lookup for the transport.</summary>
    public (string Host, int Port)? Resolve(string nodeId) =>
        _members.TryGetValue(nodeId, out var member) ? (member.Host, member.Port) : null;

    private static WireMember ToWire(ClusterMember member) => new()
    {
        NodeId = member.NodeId,
        Host = member.Host,
        Port = member.Port,
        Status = (int)member.Status,
        Incarnation = member.Incarnation,
    };

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_heartbeatLoop is not null)
        {
            try { await _heartbeatLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { /* forced */ }
        }

        _cts.Dispose();
    }
}
