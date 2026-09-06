// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Cluster;
using ActorNet.Network;
using ActorNet.Serialization;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActorNet.Tests;

/// <summary>
/// Deciding which side of a partition keeps serving.
/// </summary>
/// <remarks>
/// Both halves of a partition can see a healthy cluster from where they stand, so neither can be
/// told it is the smaller one - it has to work that out against the membership everyone last agreed
/// on. These drive one node's view directly, because two real halves of a partition on one machine
/// would need a firewall.
/// </remarks>
public sealed class SplitBrainTests
{
    /// <summary>A transport that goes nowhere: these tests drive membership by hand.</summary>
    private sealed class SilentTransport : ITransport
    {
        public int BoundPort => 1;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask SendAsync(string nodeId, WireEnvelope frame, CancellationToken cancellationToken) => default;
        public ValueTask SendToAddressAsync(string host, int port, WireEnvelope frame, CancellationToken cancellationToken) => default;
        public ValueTask DisposeAsync() => default;
    }

    private static WireEnvelope GossipFrom(string nodeId) => new()
    {
        Kind = WireKind.Gossip,
        FromNode = nodeId,
        Members = [new WireMember { NodeId = nodeId, Host = "127.0.0.1", Port = 9, Status = (int)MemberStatus.Up, Incarnation = 1 }],
    };

    private static ClusterOptions Options(SplitBrainStrategy strategy, int quorum = 0) => new()
    {
        Enabled = true,
        SplitBrainStrategy = strategy,
        StaticQuorumSize = quorum,

        // Fast enough to run as a test, same shape as the defaults: peers fall unreachable well
        // before they are taken off the ring, and the window outlasts both.
        FailureDetection = FailureDetection.Deadline,
        HeartbeatInterval = TimeSpan.FromMilliseconds(100),
        UnreachableAfter = TimeSpan.FromMilliseconds(300),
        DownAfter = TimeSpan.FromMilliseconds(600),
        SplitBrainStabilityWindow = TimeSpan.FromMilliseconds(400),
    };

    /// <summary>Brings a node up that has seen <paramref name="peers"/> and then hears nothing.</summary>
    private static async Task<(ClusterMembership Node, Task<string> Downed)> PartitionedAsync(
        string self, ClusterOptions options, params string[] peers)
    {
        var membership = new ClusterMembership(self, "127.0.0.1", 1, options, NullLogger.Instance);
        var downed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        membership.SelfDowned += reason => downed.TrySetResult(reason);

        await membership.StartAsync(new SilentTransport(), TestContext.Current.CancellationToken);

        // Heard from once, so the whole cluster is agreed on, and then silence - which from inside
        // a partition is indistinguishable from the other side having died.
        foreach (var peer in peers) membership.HandleFrame(GossipFrom(peer));

        return (membership, downed.Task);
    }

    [Theory]
    // agreed, reachable, survives
    [InlineData("a,b,c", "a,b", true)]      // two of three
    [InlineData("a,b,c", "c", false)]       // one of three
    [InlineData("a,b,c,d", "a,b", true)]    // an even split, holding the lowest id
    [InlineData("a,b,c,d", "c,d", false)]   // the other half of it
    [InlineData("a,b", "a", true)]          // two nodes, one link: somebody has to win
    [InlineData("a,b", "b", false)]
    [InlineData("a,b,c", "a,b,c", true)]    // no partition at all
    public void AMajorityIsCountedAgainstTheLastAgreedMembership(string agreed, string reachable, bool survives)
    {
        Assert.Equal(survives, ClusterMembership.SurvivesMajority(agreed.Split(','), reachable.Split(',')));
    }

    [Fact]
    public void NodesThatJoinedAfterTheSplitDoNotGetAVote()
    {
        // Otherwise a minority could manufacture a majority of itself by starting nodes, which is
        // the one thing a partitioned side can always do.
        Assert.False(ClusterMembership.SurvivesMajority(["a", "b", "c"], ["c", "new-1", "new-2"]));
    }

    [Fact]
    public void WithNothingAgreedYetEverySideSurvives()
    {
        // A node that has never seen a full cluster has nothing to be a minority of.
        Assert.True(ClusterMembership.SurvivesMajority([], ["a"]));
    }

    [Fact]
    public async Task TheMinoritySideTakesItselfDown()
    {
        var (node, downed) = await PartitionedAsync("n-c", Options(SplitBrainStrategy.KeepMajority), "n-a", "n-b");
        await using var _ = node;

        var reason = await downed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Contains("1 of 3", reason);
        Assert.Equal(MemberStatus.Down, node.Members.Single(m => m.NodeId == "n-c").Status);
    }

    [Fact]
    public async Task TheMajoritySideKeepsServing()
    {
        var options = Options(SplitBrainStrategy.KeepMajority);
        var membership = new ClusterMembership("m-a", "127.0.0.1", 1, options, NullLogger.Instance);
        await using var _ = membership;

        var downed = false;
        membership.SelfDowned += _ => downed = true;
        await membership.StartAsync(new SilentTransport(), TestContext.Current.CancellationToken);

        membership.HandleFrame(GossipFrom("m-b"));
        membership.HandleFrame(GossipFrom("m-c"));

        // Keep one peer alive across the window while the other falls silent: two of three.
        var until = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < until)
        {
            membership.HandleFrame(GossipFrom("m-b"));
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.False(downed, "a majority should not take itself down");
        Assert.Equal(MemberStatus.Up, membership.Members.Single(m => m.NodeId == "m-a").Status);
    }

    [Fact]
    public async Task AStaticQuorumIsCountedAgainstTheConfiguredSize()
    {
        var (node, downed) = await PartitionedAsync("q-a", Options(SplitBrainStrategy.StaticQuorum, quorum: 3), "q-b", "q-c");
        await using var _ = node;

        var reason = await downed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Contains("StaticQuorum", reason);
    }

    [Fact]
    public async Task WithNoStrategyBothSidesKeepServing()
    {
        // The default. Availability over consistency is a decision an operator makes, and this
        // records that not making it leaves the split brain in place.
        var (node, downed) = await PartitionedAsync("d-c", Options(SplitBrainStrategy.None), "d-a", "d-b");
        await using var _ = node;

        var fired = await Task.WhenAny(downed, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.NotSame(downed, fired);
        Assert.Equal(MemberStatus.Up, node.Members.Single(m => m.NodeId == "d-c").Status);
    }

    [Fact]
    public async Task APeerThatLeftGracefullyIsNotHalfOfAPartition()
    {
        var options = Options(SplitBrainStrategy.KeepMajority);
        var membership = new ClusterMembership("g-a", "127.0.0.1", 1, options, NullLogger.Instance);
        await using var _ = membership;

        var downed = false;
        membership.SelfDowned += _ => downed = true;
        await membership.StartAsync(new SilentTransport(), TestContext.Current.CancellationToken);

        membership.HandleFrame(GossipFrom("g-b"));
        membership.HandleFrame(GossipFrom("g-c"));
        membership.HandleFrame(new WireEnvelope { Kind = WireKind.Leave, FromNode = "g-b" });
        membership.HandleFrame(new WireEnvelope { Kind = WireKind.Leave, FromNode = "g-c" });

        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Scaling a cluster down to one node is not a partition, and a node that shut itself down
        // over an orderly departure would make rolling a cluster impossible.
        Assert.False(downed, "a graceful departure should not read as a lost half");
    }

    [Fact]
    public void AStaticQuorumWithNoSizeIsRefused()
    {
        var options = new ClusterOptions { Enabled = true, SplitBrainStrategy = SplitBrainStrategy.StaticQuorum };

        var error = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
        Assert.Equal("StaticQuorumSize", error.ParamName);
    }
}
