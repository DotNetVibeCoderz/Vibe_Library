// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Client;
using ActorNet.Cluster;

namespace ActorNet.Tests;

/// <summary>
/// A client that works out which node owns a key and sends straight to it.
/// </summary>
/// <remarks>
/// <para>
/// A node forwards a frame that arrived from something that is not a member, once, to the node that
/// owns the key - so every client is correct in a cluster whether it routes or not. Routing saves
/// the hop, which is all it saves.
/// </para>
/// <para>
/// The forwarding is not symmetric with a peer's frame, and deliberately so: a peer routed with its
/// own view of the ring, and bouncing that onward is what risks a loop between two nodes mid
/// rebalance. Only a sender absent from the member table is treated as unrouted.
/// </para>
/// </remarks>
public sealed class ClusterAwareClientTests
{
    private static ActorNetClient ClientFor(bool clusterAware, params ActorSystem[] nodes)
    {
        var client = new ActorNetClient(nodes.Select(n => $"127.0.0.1:{n.BoundPort}")) { ClusterAware = clusterAware };
        client.RegisterMessage<Add>().RegisterMessage<GetTotal>().RegisterMessage<Total>();
        return client;
    }

    private static async Task<(ActorSystem First, ActorSystem Second)> PairAsync(TestHarness harness, string prefix)
    {
        var first = await harness.NetworkedAsync($"{prefix}-a", seeds: []);
        var second = await harness.NetworkedAsync($"{prefix}-b", seeds: [$"127.0.0.1:{first.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => first.Cluster.Members.Count == 2 && second.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        await TestHarness.AssertRingsAgreeAsync(first, second);
        return (first, second);
    }

    [Fact]
    public async Task AClusterAwareClientConnectsToTheOwnerItself()
    {
        await using var harness = new TestHarness();
        var (first, second) = await PairAsync(harness, "route");

        // Connected only to the first node, and deliberately given a key the second one owns.
        await using var client = ClientFor(clusterAware: true, first);

        var theirs = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"route-{i}"))
            .First(id => first.Cluster.OwnerOf(id) == "route-b");

        await client.TellAsync(theirs, new Add(7));

        await TestHarness.AssertEventuallyAsync(
            () => second.LocalActors.Contains(theirs),
            "the actor should have been activated on its owner", TimeSpan.FromSeconds(15));

        // The saving is the hop: a second connection was opened straight to the owner rather than
        // the message being forwarded by the node the client happened to reach first.
        Assert.Contains($"127.0.0.1:{second.BoundPort}", client.ConnectedNodes);
    }

    [Fact]
    public async Task AClientThatDoesNotRouteIsForwardedToTheOwner()
    {
        await using var harness = new TestHarness();
        var (first, second) = await PairAsync(harness, "plain");

        await using var client = ClientFor(clusterAware: false, first);

        var theirs = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"plain-{i}"))
            .First(id => first.Cluster.OwnerOf(id) == "plain-b");

        await client.TellAsync(theirs, new Add(3));

        // The node the client reached does not own this key, and a client cannot route - so the
        // node forwards rather than activating the actor in the wrong place. Delivering it here
        // would leave the cluster holding two activations of one address.
        await TestHarness.AssertEventuallyAsync(
            () => second.LocalActors.Contains(theirs),
            "the owner should have been given the message", TimeSpan.FromSeconds(15));

        Assert.DoesNotContain(theirs, first.LocalActors);

        // Without routing there is still only one connection. The forwarding is what costs the hop
        // that ClusterAware exists to save.
        Assert.Single(client.ConnectedNodes);
    }

    [Fact]
    public async Task AnAskFromAnUnroutedClientComesBackThroughTheNodeItAsked()
    {
        await using var harness = new TestHarness();
        var (first, second) = await PairAsync(harness, "proxy");

        await using var client = ClientFor(clusterAware: false, first);

        var theirs = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"proxy-{i}"))
            .First(id => first.Cluster.OwnerOf(id) == "proxy-b");

        await client.TellAsync(theirs, new Add(6));

        // The harder half of forwarding. The owner has never heard of this client and has no
        // connection to answer on, so the node that forwarded the question has to carry the answer
        // back - which it can only do by having named itself as the reply address.
        var total = await client.AskAsync<Total>(theirs, new GetTotal(), TimeSpan.FromSeconds(15));

        Assert.Equal(6, total.Value);
        Assert.Contains(theirs, second.LocalActors);
    }

    [Fact]
    public async Task APeersMessageIsStillHandledWhereItArrives()
    {
        await using var harness = new TestHarness();
        var (first, second) = await PairAsync(harness, "peer");

        var theirs = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"peer-{i}"))
            .First(id => first.Cluster.OwnerOf(id) == "peer-b");

        // Sent from a node rather than a client, so it arrives already routed. Forwarding a peer's
        // message is the thing that risks a loop between two nodes disagreeing mid-rebalance, and
        // only a sender that is not a member is treated as unrouted.
        await first.TellAsync(theirs, new Add(4));

        await TestHarness.AssertEventuallyAsync(
            () => second.LocalActors.Contains(theirs),
            "a peer routes for itself", TimeSpan.FromSeconds(15));

        var total = await first.AskAsync<Total>(theirs, new GetTotal(), TimeSpan.FromSeconds(15));
        Assert.Equal(4, total.Value);
    }

    [Fact]
    public async Task AnAskIsAnsweredOnWhicheverConnectionItWentOut()
    {
        await using var harness = new TestHarness();
        var (first, second) = await PairAsync(harness, "reply");

        await using var client = ClientFor(clusterAware: true, first);

        var theirs = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"reply-{i}"))
            .First(id => first.Cluster.OwnerOf(id) == "reply-b");

        await client.TellAsync(theirs, new Add(11));

        // The reply comes back down the second connection, not the one the client opened first.
        var total = await client.AskAsync<Total>(theirs, new GetTotal(), TimeSpan.FromSeconds(15));
        Assert.Equal(11, total.Value);

        _ = second;
    }

    [Fact]
    public async Task AClientKeepsWorkingWhenTheOwnerGoesAway()
    {
        await using var harness = new TestHarness();
        var (first, second) = await PairAsync(harness, "gone");

        await using var client = ClientFor(clusterAware: true, first);

        var theirs = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"gone-{i}"))
            .First(id => first.Cluster.OwnerOf(id) == "gone-b");

        await client.TellAsync(theirs, new Add(1));
        Assert.Contains($"127.0.0.1:{second.BoundPort}", client.ConnectedNodes);

        await second.StopAsync();

        await TestHarness.AssertEventuallyAsync(
            () => first.Cluster.IsSingleNode,
            "the survivor should own the whole ring", TimeSpan.FromSeconds(20));

        // The view still names a node that has gone. Routing by it is an optimisation, so this has
        // to fall back rather than fail: the survivor owns the key now and answers for it.
        var total = await client.AskAsync<Total>(theirs, new GetTotal(), TimeSpan.FromSeconds(20));
        Assert.True(total.Value >= 0);
    }

    [Fact]
    public async Task AStandaloneNodeNeedsNoSecondConnection()
    {
        await using var harness = new TestHarness();
        var system = await harness.NetworkedAsync("alone-a", seeds: []);

        await using var client = ClientFor(clusterAware: true, system);

        var id = ActorId.For<CounterActor>("alone");
        await client.TellAsync(id, new Add(5));

        var total = await client.AskAsync<Total>(id, new GetTotal(), TimeSpan.FromSeconds(10));
        Assert.Equal(5, total.Value);

        // The owner is the node it is already talking to. A second connection to the same node
        // would double the sockets for nothing.
        Assert.Single(client.ConnectedNodes);
    }
}
