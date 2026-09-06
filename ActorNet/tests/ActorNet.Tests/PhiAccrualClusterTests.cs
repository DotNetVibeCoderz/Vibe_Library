// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Cluster;
using ActorNet.Network;
using ActorNet.Serialization;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActorNet.Tests;

/// <summary>
/// The adaptive detector driving a running cluster, not just the arithmetic.
/// </summary>
/// <remarks>
/// The harness pins its nodes to <see cref="FailureDetection.Deadline"/> so membership assertions
/// land on a known second. These opt back in, because a detector nothing routes through is a
/// calculator rather than a failure detector.
/// </remarks>
public sealed class PhiAccrualClusterTests
{
    private static void UsePhi(ActorSystemOptions options)
    {
        options.Cluster.FailureDetection = FailureDetection.PhiAccrual;
        options.Cluster.HeartbeatInterval = TimeSpan.FromMilliseconds(200);

        // The defaults are sized for a wide-area link; these are the same shape, one order faster,
        // so a test does not spend four seconds waiting for the allowance to elapse.
        options.Cluster.AcceptableHeartbeatPause = TimeSpan.FromMilliseconds(300);
        options.Cluster.MinimumStandardDeviation = TimeSpan.FromMilliseconds(50);
    }

    [Fact]
    public async Task AHealthyPeerIsNeverSuspected()
    {
        await using var harness = new TestHarness();

        var seed = await harness.NetworkedAsync("phi-seed", seeds: [], configure: UsePhi);
        var joiner = await harness.NetworkedAsync("phi-joiner", seeds: [$"127.0.0.1:{seed.BoundPort}"], configure: UsePhi);

        await TestHarness.AssertEventuallyAsync(
            () => joiner.Cluster.Members.Count == 2 && seed.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        // Several dozen heartbeats of a working link. A detector that suspects here would take a
        // healthy cluster apart on its own.
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.All(joiner.Cluster.Members, m => Assert.Equal(MemberStatus.Up, m.Status));
        Assert.All(seed.Cluster.Members, m => Assert.Equal(MemberStatus.Up, m.Status));
    }

    [Fact]
    public async Task APeerThatFallsSilentIsTakenOffTheRing()
    {
        var options = new ClusterOptions
        {
            Enabled = true,
            FailureDetection = FailureDetection.PhiAccrual,
            HeartbeatInterval = TimeSpan.FromMilliseconds(100),
            AcceptableHeartbeatPause = TimeSpan.FromMilliseconds(100),
            MinimumStandardDeviation = TimeSpan.FromMilliseconds(20),

            // Left at their defaults on purpose: ten and thirty seconds. Nothing in this test waits
            // that long, so a deadline detector could not pass it - which is what makes it evidence
            // that phi is the thing driving membership.
        };

        await using var membership = new ClusterMembership("watcher", "127.0.0.1", 1, options, NullLogger.Instance);
        await membership.StartAsync(new SilentTransport(), TestContext.Current.CancellationToken);

        // Dying rather than leaving. A node that shuts down gracefully announces it and is taken
        // off the ring immediately; the case a failure detector exists for is the one where nobody
        // says anything.
        for (var i = 0; i < 10; i++)
        {
            membership.HandleFrame(GossipFrom("ghost"));
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Equal(MemberStatus.Up, Status(membership, "ghost"));

        await TestHarness.AssertEventuallyAsync(
            () => Status(membership, "ghost") == MemberStatus.Down,
            "a peer that stops beating should end up off the ring", TimeSpan.FromSeconds(5));

        Assert.True(membership.IsSingleNode, "the survivor should own the whole ring again");
    }

    private static MemberStatus Status(ClusterMembership membership, string nodeId) =>
        membership.Members.Single(m => m.NodeId == nodeId).Status;

    private static WireEnvelope GossipFrom(string nodeId) => new()
    {
        Kind = WireKind.Gossip,
        FromNode = nodeId,
        Members = [new WireMember { NodeId = nodeId, Host = "127.0.0.1", Port = 2, Status = (int)MemberStatus.Up, Incarnation = 1 }],
    };

    /// <summary>A transport that goes nowhere: this test drives membership by hand.</summary>
    private sealed class SilentTransport : ITransport
    {
        public int BoundPort => 1;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask SendAsync(string nodeId, WireEnvelope frame, CancellationToken cancellationToken) => default;
        public ValueTask SendToAddressAsync(string host, int port, WireEnvelope frame, CancellationToken cancellationToken) => default;
        public ValueTask DisposeAsync() => default;
    }

    [Fact]
    public async Task ThresholdsAreValidatedTogether()
    {
        var options = new ClusterOptions
        {
            Enabled = true,
            PhiUnreachableThreshold = 12,
            PhiDownThreshold = 8,
        };

        var error = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Equal("PhiDownThreshold", error.ParamName);
    }
}
