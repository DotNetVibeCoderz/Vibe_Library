// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Cluster;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ActorNet.AspNetCore;

/// <summary>
/// Reports whether this node is serving, and what it can see of the cluster.
/// </summary>
/// <remarks>
/// <para>
/// A health check that only answered "the process is up" would be worse than none: an orchestrator
/// would keep sending traffic to a node that has lost the cluster and is answering for keys it does
/// not own. What matters is whether this node can still reach the peers it believes exist.
/// </para>
/// <para>
/// Unreachable peers are <em>degraded</em>, not unhealthy. Unreachable means "missed a few beats,
/// still on the ring" - the node is serving its own keys correctly and restarting it would turn a
/// blip into a rebalance. Only a node that has taken itself off the ring is unhealthy.
/// </para>
/// </remarks>
public sealed class ActorNetHealthCheck : IHealthCheck
{
    private readonly IActorSystem _system;

    public ActorNetHealthCheck(IActorSystem system) => _system = system;

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var cluster = _system.Cluster;
        var members = cluster.Members;

        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["node"] = cluster.SelfNodeId,
            ["members"] = members.Count,
            ["reachable"] = members.Count(m => m.Status == MemberStatus.Up),
        };

        var self = members.FirstOrDefault(m => m.NodeId == cluster.SelfNodeId);

        // A node that has taken itself off the ring - split-brain resolution, or a leave already
        // announced - is not going to serve anything. That is the one state worth failing on.
        if (self is not null && self.Status is MemberStatus.Down or MemberStatus.Leaving)
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"This node is {self.Status} in its own view; it is no longer on the ring.", data: data));

        var unreachable = members.Where(m => m.NodeId != cluster.SelfNodeId && m.Status != MemberStatus.Up).ToArray();
        if (unreachable.Length > 0)
        {
            data["unreachable"] = string.Join(", ", unreachable.Select(m => $"{m.NodeId}:{m.Status}"));

            return Task.FromResult(HealthCheckResult.Degraded(
                $"{unreachable.Length} of {members.Count} members are not reachable from here.", data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            cluster.IsSingleNode ? "Serving; no peers." : $"Serving; {members.Count} members reachable.", data));
    }
}
