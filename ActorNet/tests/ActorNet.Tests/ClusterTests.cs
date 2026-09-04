// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Cluster;

namespace ActorNet.Tests;

public sealed class ClusterTests
{
    [Fact]
    public async Task TwoNodesFindEachOtherThroughASeed()
    {
        await using var harness = new TestHarness();

        var first = await harness.NetworkedAsync("node-1", seeds: []);
        var second = await harness.NetworkedAsync("node-2", seeds: [$"127.0.0.1:{first.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => first.Cluster.Members.Count == 2 && second.Cluster.Members.Count == 2,
            "both nodes should know about each other after the join handshake",
            TimeSpan.FromSeconds(15));

        Assert.Contains(first.Cluster.Members, m => m.NodeId == "node-2");
        Assert.Contains(second.Cluster.Members, m => m.NodeId == "node-1");
    }

    [Fact]
    public async Task BothNodesAgreeOnWhoOwnsEachKey()
    {
        await using var harness = new TestHarness();

        var first = await harness.NetworkedAsync("node-1", seeds: []);
        var second = await harness.NetworkedAsync("node-2", seeds: [$"127.0.0.1:{first.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => first.Cluster.Members.Count == 2 && second.Cluster.Members.Count == 2,
            "the cluster should have converged", TimeSpan.FromSeconds(15));

        // Disagreement here would mean two activations of the same actor, one per node - the
        // failure that the process-independent hash exists to prevent.
        foreach (var i in Enumerable.Range(0, 200))
        {
            var id = ActorId.For<CounterActor>($"agree-{i}");
            Assert.Equal(first.Cluster.OwnerOf(id), second.Cluster.OwnerOf(id));
        }
    }

    [Fact]
    public async Task AMessageForARemoteKeyIsHandledOnItsOwner()
    {
        await using var harness = new TestHarness();

        var first = await harness.NetworkedAsync("node-1", seeds: []);
        var second = await harness.NetworkedAsync("node-2", seeds: [$"127.0.0.1:{first.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => first.Cluster.Members.Count == 2 && second.Cluster.Members.Count == 2,
            "the cluster should have converged", TimeSpan.FromSeconds(15));

        // Find a key the first node does not own, so the send genuinely crosses the network.
        var remote = Enumerable.Range(0, 500)
            .Select(i => ActorId.For<CounterActor>($"remote-{i}"))
            .First(id => first.Cluster.OwnerOf(id) == "node-2");

        await first.TellAsync(remote, new Add(4));

        await TestHarness.AssertEventuallyAsync(() => second.LocalActors.Contains(remote),
            $"{remote} should have been activated on node-2, its owner", TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(remote, first.LocalActors);
    }

    [Fact]
    public async Task AnAskAcrossNodesGetsItsReplyBack()
    {
        await using var harness = new TestHarness();

        var first = await harness.NetworkedAsync("node-1", seeds: []);
        var second = await harness.NetworkedAsync("node-2", seeds: [$"127.0.0.1:{first.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => first.Cluster.Members.Count == 2 && second.Cluster.Members.Count == 2,
            "the cluster should have converged", TimeSpan.FromSeconds(15));

        var remote = Enumerable.Range(0, 500)
            .Select(i => ActorId.For<CounterActor>($"ask-{i}"))
            .First(id => first.Cluster.OwnerOf(id) == "node-2");

        await first.TellAsync(remote, new Add(11));
        var total = await first.AskAsync<Total>(remote, new GetTotal(), TimeSpan.FromSeconds(20));

        // The reply travelled node-2 -> node-1 on node-2's own outbound connection, matched by
        // correlation id rather than by which socket it arrived on.
        Assert.Equal(11, total.Value);
        Assert.True(first.Metrics.Snapshot(false).RemoteSent > 0);
    }

    [Fact]
    public async Task ANewNodeTakesOverPartOfTheKeyspace()
    {
        await using var harness = new TestHarness();

        var first = await harness.NetworkedAsync("node-1", seeds: []);

        // Activate a spread of actors while node-1 is alone and owns everything.
        for (var i = 0; i < 60; i++) await first.TellAsync(ActorId.For<CounterActor>($"spread-{i}"), new Add(1));
        await TestHarness.AssertEventuallyAsync(() => first.LocalActors.Count == 60, "all 60 should be local while alone");

        var second = await harness.NetworkedAsync("node-2", seeds: [$"127.0.0.1:{first.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => first.Cluster.Members.Count == 2 && second.Cluster.Members.Count == 2,
            "the cluster should have converged", TimeSpan.FromSeconds(15));

        // Handing off is deactivating: state was flushed and the next message re-activates the
        // actor on its new owner. Roughly half should have moved.
        await TestHarness.AssertEventuallyAsync(() => first.LocalActors.Count < 60,
            "node-1 should have handed off the keys that now belong to node-2", TimeSpan.FromSeconds(15));

        Assert.InRange(first.LocalActors.Count, 1, 59);
    }

    [Fact]
    public async Task AStandaloneNodeOwnsEverythingWithoutAskingAnyone()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        Assert.True(system.Cluster.IsSingleNode);
        Assert.Single(system.Cluster.Members);

        foreach (var i in Enumerable.Range(0, 50))
            Assert.True(system.Cluster.IsLocal(ActorId.For<CounterActor>($"solo-{i}")));
    }

    [Fact]
    public void ClusterOptionsRejectTimingsThatWouldEvictHealthyNodes()
    {
        var options = new ClusterOptions
        {
            Enabled = true,
            UnreachableAfter = TimeSpan.FromSeconds(10),
            DownAfter = TimeSpan.FromSeconds(5),
        };

        // Down before unreachable would take a node off the ring on the first missed beat, which
        // turns a GC pause into a cluster-wide rebalance.
        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void ClusterOptionsRejectAHeartbeatSlowerThanItsOwnDeadline()
    {
        var options = new ClusterOptions
        {
            Enabled = true,
            HeartbeatInterval = TimeSpan.FromSeconds(30),
            UnreachableAfter = TimeSpan.FromSeconds(10),
            DownAfter = TimeSpan.FromSeconds(60),
        };

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Theory]
    [InlineData("127.0.0.1:9000", "127.0.0.1", 9000)]
    [InlineData("node-a.internal:7777", "node-a.internal", 7777)]
    public void SeedsParseIntoHostAndPort(string seed, string host, int port)
    {
        Assert.True(ClusterOptions.TryParseSeed(seed, out var parsedHost, out var parsedPort));
        Assert.Equal(host, parsedHost);
        Assert.Equal(port, parsedPort);
    }

    [Theory]
    [InlineData("no-port")]
    [InlineData("host:")]
    [InlineData(":9000")]
    [InlineData("host:not-a-number")]
    [InlineData("host:99999")]
    public void MalformedSeedsAreRejected(string seed)
    {
        Assert.False(ClusterOptions.TryParseSeed(seed, out _, out _));
    }
}
