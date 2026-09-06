// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using ActorNet.Cluster;
using ActorNet.Network;
using ActorNet.Serialization;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActorNet.Tests;

/// <summary>
/// Gossiping to a few peers a beat instead of all of them.
/// </summary>
/// <remarks>
/// Sending the whole table to everybody costs O(members squared) frames per interval. The
/// information still has to reach everybody, so the question these ask is not whether fewer frames
/// are sent - that is arithmetic - but whether the rotation still covers every peer, and whether a
/// cluster held together by second-hand news still converges.
/// </remarks>
public sealed class GossipFanoutTests
{
    /// <summary>A transport that records who was gossiped to and sends nothing.</summary>
    private sealed class RecordingTransport : ITransport
    {
        public ConcurrentQueue<string> Sent { get; } = new();

        public int BoundPort => 1;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask SendToAddressAsync(string host, int port, WireEnvelope frame, CancellationToken cancellationToken) => default;
        public ValueTask DisposeAsync() => default;

        public ValueTask SendAsync(string nodeId, WireEnvelope frame, CancellationToken cancellationToken)
        {
            if (frame.Kind == WireKind.Gossip) Sent.Enqueue(nodeId);
            return default;
        }
    }

    private static WireEnvelope GossipFrom(string nodeId, int port) => new()
    {
        Kind = WireKind.Gossip,
        FromNode = nodeId,
        Members = [new WireMember { NodeId = nodeId, Host = "127.0.0.1", Port = port, Status = (int)MemberStatus.Up, Incarnation = 1 }],
    };

    /// <summary>Builds a node that already knows about <paramref name="peers"/> peers.</summary>
    private static async Task<(ClusterMembership Membership, RecordingTransport Transport)> WithPeersAsync(int peers, int fanout)
    {
        var options = new ClusterOptions
        {
            Enabled = true,
            GossipFanout = fanout,

            // Long enough that the timer never fires during a test: these drive the beat by hand,
            // because a beat that arrives on its own would make the rotation impossible to assert.
            HeartbeatInterval = TimeSpan.FromMinutes(5),
        };

        var membership = new ClusterMembership("self", "127.0.0.1", 1, options, NullLogger.Instance);
        var transport = new RecordingTransport();
        await membership.StartAsync(transport, TestContext.Current.CancellationToken);

        for (var i = 0; i < peers; i++) membership.HandleFrame(GossipFrom($"peer-{i:D2}", 1000 + i));

        return (membership, transport);
    }

    [Theory]
    [InlineData(10, 3)]
    [InlineData(10, 4)]
    [InlineData(7, 2)]
    [InlineData(9, 5)]
    public async Task TheRotationReachesEveryPeer(int peers, int fanout)
    {
        var (membership, transport) = await WithPeersAsync(peers, fanout);
        await using var _ = membership;

        // Enough beats to walk the list once, whatever the remainder does.
        var rounds = (int)Math.Ceiling((double)peers / fanout);
        for (var i = 0; i < rounds; i++) await membership.GossipAsync(TestContext.Current.CancellationToken);

        var reached = transport.Sent.Distinct().ToList();
        Assert.Equal(peers, reached.Count);
        Assert.Equal(rounds * fanout, transport.Sent.Count);
    }

    [Fact]
    public async Task OneBeatCostsTheFanoutAndNotTheClusterSize()
    {
        var (membership, transport) = await WithPeersAsync(peers: 50, fanout: 4);
        await using var _ = membership;

        await membership.GossipAsync(TestContext.Current.CancellationToken);

        // The point of the whole exercise: a node in a 51-member cluster sends 4 frames a beat,
        // not 50. Everything else here is about that still being enough.
        Assert.Equal(4, transport.Sent.Count);
    }

    [Fact]
    public async Task AFanoutOfZeroMeansEveryPeer()
    {
        var (membership, transport) = await WithPeersAsync(peers: 12, fanout: 0);
        await using var _ = membership;

        await membership.GossipAsync(TestContext.Current.CancellationToken);

        Assert.Equal(12, transport.Sent.Count);
    }

    [Fact]
    public async Task ADownPeerIsNotGossipedTo()
    {
        var (membership, transport) = await WithPeersAsync(peers: 3, fanout: 0);
        await using var _ = membership;

        membership.HandleFrame(new WireEnvelope
        {
            Kind = WireKind.Leave,
            FromNode = "peer-01",
        });

        await membership.GossipAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("peer-01", transport.Sent);
        Assert.Equal(2, transport.Sent.Count);
    }

    [Fact]
    public async Task ANegativeFanoutIsRefused()
    {
        var options = new ClusterOptions { Enabled = true, GossipFanout = -1 };

        var error = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
        Assert.Equal("GossipFanout", error.ParamName);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task AClusterConvergesOnSecondHandNews()
    {
        await using var harness = new TestHarness();

        // Fanout of one: no node ever gossips to more than a single peer in a beat, so most of what
        // each node knows arrives through somebody else. If the epidemic works at all, it works
        // here; if it needed everyone to talk to everyone, this would stall at two members.
        void Sparse(ActorSystemOptions o) => o.Cluster.GossipFanout = 1;

        var seed = await harness.NetworkedAsync("fan-a", seeds: [], configure: Sparse);
        var b = await harness.NetworkedAsync("fan-b", seeds: [$"127.0.0.1:{seed.BoundPort}"], configure: Sparse);
        var c = await harness.NetworkedAsync("fan-c", seeds: [$"127.0.0.1:{seed.BoundPort}"], configure: Sparse);
        var d = await harness.NetworkedAsync("fan-d", seeds: [$"127.0.0.1:{seed.BoundPort}"], configure: Sparse);

        await TestHarness.AssertEventuallyAsync(
            () => new[] { seed, b, c, d }.All(n => n.Cluster.Members.Count == 4),
            "every node should learn about every other, even at a fanout of one",
            TimeSpan.FromSeconds(20));
    }
}
