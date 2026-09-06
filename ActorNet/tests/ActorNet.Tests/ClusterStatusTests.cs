// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Metrics;

namespace ActorNet.Tests;

/// <summary>
/// Asking the whole cluster what it is doing, not just the node you are standing on.
/// </summary>
/// <remarks>
/// The ring and the member table were always cluster-wide and the counters were not, so a console
/// could report five members and only what one of them was doing. A node buried under work looks
/// exactly like an idle one from three nodes away.
/// </remarks>
public sealed class ClusterStatusTests
{
    [Fact]
    public async Task AStandaloneNodeReportsItself()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        await system.TellAsync(ActorId.For<CounterActor>("solo"), new Add(1));
        await system.AskAsync<Total>(ActorId.For<CounterActor>("solo"), new GetTotal(), TimeSpan.FromSeconds(10));

        var status = await system.GetClusterStatusAsync();

        var only = Assert.Single(status.Nodes);
        Assert.Equal(system.NodeId, only.NodeId);
        Assert.Empty(status.Silent);
        Assert.True(status.MessagesProcessed >= 2);
    }

    [Fact]
    public async Task EveryMemberIsAskedAndTheAnswersAddUp()
    {
        await using var harness = new TestHarness();

        var a = await harness.NetworkedAsync("stat-a", seeds: []);
        var b = await harness.NetworkedAsync("stat-b", seeds: [$"127.0.0.1:{a.BoundPort}"]);
        var c = await harness.NetworkedAsync("stat-c", seeds: [$"127.0.0.1:{a.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => new[] { a, b, c }.All(n => n.Cluster.Members.Count == 3),
            "the cluster should converge", TimeSpan.FromSeconds(20));

        // Work placed by the ring, so it lands wherever it lands - which is the point: the totals
        // have to come from three nodes rather than from the one being asked.
        foreach (var i in Enumerable.Range(0, 30))
            await a.TellAsync(ActorId.For<CounterActor>($"stat-{i}"), new Add(1));

        foreach (var i in Enumerable.Range(0, 30))
            await a.AskAsync<Total>(ActorId.For<CounterActor>($"stat-{i}"), new GetTotal(), TimeSpan.FromSeconds(15));

        var status = await a.GetClusterStatusAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(3, status.Nodes.Count);
        Assert.Empty(status.Silent);
        Assert.Equal(["stat-a", "stat-b", "stat-c"], status.Nodes.Select(n => n.NodeId));

        // 30 actors, each activated somewhere in the cluster. No single node holds all of them,
        // so this only passes if the counts came back from the other two.
        Assert.Equal(30, status.ActiveActors);
        Assert.True(status.MessagesProcessed >= 60,
            $"the cluster handled {status.MessagesProcessed} messages, which is fewer than were sent");
    }

    [Fact]
    public async Task ANodeThatDoesNotAnswerIsNamedRatherThanDropped()
    {
        await using var harness = new TestHarness();

        var survivor = await harness.NetworkedAsync("quiet-a", seeds: []);
        var doomed = await harness.NetworkedAsync("quiet-b", seeds: [$"127.0.0.1:{survivor.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => survivor.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        // The transport goes, the membership has not caught up yet: the peer is still Up in the
        // table and will not answer. That gap is exactly when somebody is looking at a console.
        Assert.NotNull(doomed.Transport);
        await doomed.Transport.StopAsync(CancellationToken.None);

        var status = await survivor.GetClusterStatusAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("quiet-a", Assert.Single(status.Nodes).NodeId);
        Assert.Equal("quiet-b", Assert.Single(status.Silent));
    }

    [Fact]
    public void TheBusiestNodeIsTheNumberWorthHaving()
    {
        var idle = new NodeStatus("idle", TimeSpan.Zero, 10, 0, 1, 2, 0, 0, 0, 0, 5, 5);
        var buried = new NodeStatus("buried", TimeSpan.Zero, 10, 0, 900, 2, 40, 0, 0, 0, 5, 5);

        var status = new ClusterStatus(DateTimeOffset.UtcNow, [idle, buried], []);

        // A cluster total hides the case this exists to find: one node buried while the others
        // idle, which is a placement problem rather than a capacity one.
        Assert.Equal("buried", status.Busiest!.NodeId);
        Assert.Equal(901, status.InFlight);
        Assert.Equal(40, status.MailboxDepth);
    }
}
