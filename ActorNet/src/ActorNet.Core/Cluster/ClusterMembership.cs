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
/// to every peer it knows. That is gossip in the loose sense - it converges, and it costs
/// O(members squared) beats per interval, which is nothing at the tens-of-nodes scale this
/// targets and would need a fanout limit beyond it.
/// </para>
/// <para>
/// Failure detection is a plain deadline on last contact, not a phi-accrual detector. It is
/// honest about what it can do: it will call a node unreachable during a long GC pause, which is
/// why <see cref="ClusterOptions.UnreachableAfter"/> keeps such a node <em>on</em> the ring and
/// only <see cref="ClusterOptions.DownAfter"/> takes it off.
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

    private ITransport? _transport;
    private Task? _heartbeatLoop;
    private HashRing _ring;
    private long _incarnation = 1;

    /// <inheritdoc />
    public string SelfNodeId { get; }

    /// <inheritdoc />
    public event Action<IReadOnlyList<ClusterMember>>? MembershipChanged;

    public ClusterMembership(string nodeId, string host, int port, ClusterOptions options, ILogger logger)
    {
        SelfNodeId = nodeId;
        _options = options;
        _logger = logger;
        _members[nodeId] = new ClusterMember(nodeId, host, port, MemberStatus.Up, DateTimeOffset.UtcNow, _incarnation);
        _ring = BuildRing();
    }

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

    /// <summary>Updates this node's advertised port once the transport has bound one.</summary>
    public void SetSelfPort(int port)
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

        await JoinSeedsAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task JoinSeedsAsync(CancellationToken cancellationToken)
    {
        var self = _members[SelfNodeId];
        var frame = new WireEnvelope
        {
            Kind = WireKind.Join,
            FromNode = SelfNodeId,
            Members = [ToWire(self)],
        };

        var reached = 0;
        foreach (var seed in _options.Seeds)
        {
            if (!ClusterOptions.TryParseSeed(seed, out var host, out var port)) continue;
            if (port == self.Port && IsSelfHost(host)) continue;

            try
            {
                await _transport!.SendToAddressAsync(host, port, frame, cancellationToken).ConfigureAwait(false);
                reached++;
            }
            catch (Exception ex)
            {
                // A seed that is down is normal - the first node to start has none reachable.
                _logger.LogDebug(ex, "Seed {Seed} did not answer the join.", seed);
            }
        }

        _logger.LogInformation("Join sent to {Reached} of {Total} seeds.", reached, _options.Seeds.Count);
    }

    private bool IsSelfHost(string host) =>
        string.Equals(host, _members[SelfNodeId].Host, StringComparison.OrdinalIgnoreCase) ||
        host is "127.0.0.1" or "localhost" or "0.0.0.0";

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.HeartbeatInterval);
        while (await SafeWaitAsync(timer, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                DetectFailures();
                await GossipAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task GossipAsync(CancellationToken cancellationToken)
    {
        var frame = new WireEnvelope
        {
            Kind = WireKind.Gossip,
            FromNode = SelfNodeId,
            Members = _members.Values.Select(ToWire).ToList(),
        };

        foreach (var member in _members.Values)
        {
            if (member.NodeId == SelfNodeId || member.Status == MemberStatus.Down) continue;
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

    private void DetectFailures()
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;

        foreach (var (id, member) in _members)
        {
            if (id == SelfNodeId) continue;

            var silence = now - member.LastSeen;
            var status = silence > _options.DownAfter ? MemberStatus.Down
                : silence > _options.UnreachableAfter ? MemberStatus.Unreachable
                : member.Status == MemberStatus.Unreachable ? MemberStatus.Up
                : member.Status;

            if (status == member.Status) continue;

            _members[id] = member with { Status = status };
            changed = true;
            _logger.LogWarning("Member {NodeId} is now {Status} after {Silence:N1}s of silence.", id, status, silence.TotalSeconds);
        }

        if (changed) RebuildRing();
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
            var revived = member.Status is MemberStatus.Unreachable or MemberStatus.Down;
            _members[nodeId] = member with { LastSeen = DateTimeOffset.UtcNow, Status = revived ? MemberStatus.Up : member.Status };
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
