// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Cluster;

namespace ActorNet.Tests;

/// <summary>
/// Taking the nodes down one at a time, which is what makes a rolling restart rolling.
/// </summary>
/// <remarks>
/// Nothing otherwise stops two nodes stopping at the same moment. The keys of the first move to the
/// second while the second is on its way out, so they move twice; with a quorum configured, the pair
/// can take the cluster below it in a single step.
/// </remarks>
public sealed class CoordinatedLeaveTests
{
    private static void Coordinated(ActorSystemOptions options)
    {
        options.Cluster.CoordinatedLeave = true;
        options.Cluster.LeaveTokenWait = TimeSpan.FromSeconds(20);
        options.Cluster.LeaveTokenLease = TimeSpan.FromSeconds(30);
    }

    [Fact]
    public void TheLowestNodeIdHandsOutTheToken()
    {
        var token = new LeaveTokenHolder(TimeProvider.System);
        var lease = TimeSpan.FromMinutes(1);

        Assert.True(token.Decide(new LeaveRequest("node-a", false), lease).Granted);

        // One at a time is the whole feature.
        var refused = token.Decide(new LeaveRequest("node-b", false), lease);
        Assert.False(refused.Granted);
        Assert.Equal("node-a", refused.HeldBy);
        Assert.True(refused.RetryAfter > TimeSpan.Zero, "a refusal should say when to ask again");

        Assert.True(token.Decide(new LeaveRequest("node-a", true), lease).Granted);
        Assert.True(token.Decide(new LeaveRequest("node-b", false), lease).Granted);
    }

    [Fact]
    public void AskingTwiceIsNotAskingForASecondToken()
    {
        var token = new LeaveTokenHolder(TimeProvider.System);
        var lease = TimeSpan.FromMinutes(1);

        Assert.True(token.Decide(new LeaveRequest("node-a", false), lease).Granted);

        // A node whose reply was lost asks again. Refusing it its own token would leave it waiting
        // for itself until the lease ran out.
        Assert.True(token.Decide(new LeaveRequest("node-a", false), lease).Granted);
    }

    [Fact]
    public void OnlyTheHolderCanHandTheTokenBack()
    {
        var token = new LeaveTokenHolder(TimeProvider.System);
        var lease = TimeSpan.FromMinutes(1);

        Assert.True(token.Decide(new LeaveRequest("node-a", false), lease).Granted);
        token.Decide(new LeaveRequest("node-b", true), lease);

        // node-b released a token it never held. If that had worked, a node that lost the race
        // would be able to hand back the token the winner is relying on.
        Assert.Equal("node-a", token.Holder);
        Assert.False(token.Decide(new LeaveRequest("node-c", false), lease).Granted);
    }

    [Fact]
    public void ATokenHeldByANodeThatDiedExpires()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var token = new LeaveTokenHolder(clock);
        var lease = TimeSpan.FromSeconds(30);

        Assert.True(token.Decide(new LeaveRequest("node-a", false), lease).Granted);

        // node-a was killed midway and will never release it. Without the lease, nothing else in
        // the cluster could ever leave until the coordinator itself restarted.
        Assert.False(token.Decide(new LeaveRequest("node-b", false), lease).Granted);

        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Null(token.Holder);
        Assert.True(token.Decide(new LeaveRequest("node-b", false), lease).Granted);
    }

    [Fact]
    public async Task EveryNodeAgreesWhoTheCoordinatorIs()
    {
        await using var harness = new TestHarness();

        var first = await harness.NetworkedAsync("coord-a", seeds: [], configure: Coordinated);
        var second = await harness.NetworkedAsync("coord-b", seeds: [$"127.0.0.1:{first.BoundPort}"], configure: Coordinated);
        var third = await harness.NetworkedAsync("coord-c", seeds: [$"127.0.0.1:{first.BoundPort}"], configure: Coordinated);

        await TestHarness.AssertEventuallyAsync(
            () => first.Cluster.Members.Count == 3 && second.Cluster.Members.Count == 3 && third.Cluster.Members.Count == 3,
            "the cluster should converge", TimeSpan.FromSeconds(20));

        await TestHarness.AssertRingsAgreeAsync(first, second, third);

        // Computed from each node's own member table rather than elected, so there is nothing to
        // run and nothing to fail over - and every node has to reach the same answer.
        foreach (var node in new[] { first, second, third })
            Assert.Equal("coord-a", ((ClusterMembership)node.Cluster).Coordinator);
    }

    [Fact]
    public async Task TheCoordinatorCanLeaveItself()
    {
        await using var harness = new TestHarness();

        var coordinator = await harness.NetworkedAsync("solo-a", seeds: [], configure: Coordinated);
        var other = await harness.NetworkedAsync("solo-b", seeds: [$"127.0.0.1:{coordinator.BoundPort}"], configure: Coordinated);

        await TestHarness.AssertEventuallyAsync(
            () => coordinator.Cluster.Members.Count == 2 && other.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        await TestHarness.AssertRingsAgreeAsync(coordinator, other);
        Assert.Equal("solo-a", ((ClusterMembership)coordinator.Cluster).Coordinator);

        // The node handing out the token is the one leaving. It asks itself, which is the case a
        // protocol with an elected coordinator would have had to handle specially.
        await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));

        await TestHarness.AssertEventuallyAsync(
            () => other.Cluster.IsSingleNode,
            "the survivor should own the whole ring", TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task TwoNodesLeavingAtOnceAreBothHeldBackUntilTheTokenIsFree()
    {
        await using var harness = new TestHarness();

        static void ShortLease(ActorSystemOptions options)
        {
            Coordinated(options);
            options.Cluster.LeaveTokenLease = TimeSpan.FromSeconds(4);
            options.Cluster.LeaveTokenWait = TimeSpan.FromSeconds(25);
        }

        var keeper = await harness.NetworkedAsync("roll-a", seeds: [], configure: ShortLease);
        var first = await harness.NetworkedAsync("roll-b", seeds: [$"127.0.0.1:{keeper.BoundPort}"], configure: ShortLease);
        var second = await harness.NetworkedAsync("roll-c", seeds: [$"127.0.0.1:{keeper.BoundPort}"], configure: ShortLease);

        await TestHarness.AssertEventuallyAsync(
            () => keeper.Cluster.Members.Count == 3 && first.Cluster.Members.Count == 3 && second.Cluster.Members.Count == 3,
            "the cluster should converge", TimeSpan.FromSeconds(20));

        await TestHarness.AssertRingsAgreeAsync(keeper, first, second);

        // roll-a hands out the token and is staying, so both leavers go through it.
        var coordinator = (ClusterMembership)keeper.Cluster;
        Assert.Equal("roll-a", coordinator.Coordinator);

        // Somebody else already holds it, so neither node can leave until that lease runs out.
        // Timing the pair is what makes this test discriminating: without coordination two
        // concurrent stops finish in a fraction of a second.
        Assert.True(coordinator.DecideLeave(new LeaveRequest("a-ghost", false)).Granted);

        var started = DateTimeOffset.UtcNow;
        await Task.WhenAll(first.StopAsync(), second.StopAsync()).WaitAsync(TimeSpan.FromSeconds(60));
        var took = DateTimeOffset.UtcNow - started;

        Assert.True(took >= TimeSpan.FromSeconds(4),
            $"both nodes should have waited for the token to come free, but the pair took {took}");

        await TestHarness.AssertEventuallyAsync(
            () => keeper.Cluster.IsSingleNode,
            "the survivor should own the whole ring once both have gone", TimeSpan.FromSeconds(25));

        // Each of them handed it back on the way out, so an operator restarting them finds it free.
        Assert.Null(coordinator.LeaveTokenHeldBy);
    }

    [Fact]
    public async Task ANodeThatCannotGetTheTokenStillLeaves()
    {
        await using var harness = new TestHarness();

        var keeper = await harness.NetworkedAsync("stuck-a", seeds: [], configure: o =>
        {
            Coordinated(o);
            // Long enough that the holder below never releases it within this test.
            o.Cluster.LeaveTokenLease = TimeSpan.FromMinutes(10);
        });

        var blocked = await harness.NetworkedAsync("stuck-b", seeds: [$"127.0.0.1:{keeper.BoundPort}"], configure: o =>
        {
            Coordinated(o);
            o.Cluster.LeaveTokenWait = TimeSpan.FromSeconds(2);
            o.Cluster.LeaveTokenLease = TimeSpan.FromMinutes(10);
        });

        await TestHarness.AssertEventuallyAsync(
            () => keeper.Cluster.Members.Count == 2 && blocked.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        await TestHarness.AssertRingsAgreeAsync(keeper, blocked);

        // Somebody else is holding the token and is never going to give it back.
        var coordinator = (ClusterMembership)keeper.Cluster;
        Assert.True(coordinator.DecideLeave(new LeaveRequest("a-ghost", false)).Granted);

        // A shutdown that blocks forever is worse than an uncoordinated one: whatever asked this
        // node to stop kills it instead, and a killed node announces nothing at all.
        var started = DateTimeOffset.UtcNow;
        await blocked.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var took = DateTimeOffset.UtcNow - started;

        Assert.True(took >= TimeSpan.FromSeconds(2), $"it should have waited out its budget, but took {took}");

        await TestHarness.AssertEventuallyAsync(
            () => keeper.Cluster.IsSingleNode,
            "it should have announced its departure anyway", TimeSpan.FromSeconds(20));
    }
}

/// <summary>A clock the test moves by hand, for the lease expiry.</summary>
internal sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
