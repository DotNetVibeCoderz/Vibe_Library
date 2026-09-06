// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Tests;

/// <summary>
/// Handing actors to their next owner instead of making it find them.
/// </summary>
/// <remarks>
/// Rebalancing works by deactivating an actor and letting the next message reactivate it from the
/// store, so after a planned restart the first message to every key that moved pays a read - all of
/// them at once, at the moment traffic arrives. A leaving node knows which keys are moving and
/// where to, and can say so before it goes.
/// </remarks>
public sealed class WarmHandoffTests
{
    [Fact]
    public async Task TheSuccessorHasTheActorsBeforeTheFirstMessageArrives()
    {
        await using var harness = new TestHarness();

        var leaving = await harness.NetworkedAsync("warm-a", seeds: []);
        var staying = await harness.NetworkedAsync("warm-b", seeds: [$"127.0.0.1:{leaving.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => leaving.Cluster.Members.Count == 2 && staying.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        // Activate a handful of actors on the node that is about to go.
        var mine = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"warm-{i}"))
            .Where(id => leaving.Cluster.OwnerOf(id) == "warm-a")
            .Take(5)
            .ToArray();

        Assert.NotEmpty(mine);

        foreach (var id in mine)
            await leaving.AskAsync<Total>(id, new GetTotal(), TimeSpan.FromSeconds(10));

        var before = staying.LocalActors.Count;

        await leaving.StopAsync();

        // The successor should have them without anything having been sent to them. Waiting is for
        // the messages to arrive and be handled, not for traffic to trigger anything.
        await TestHarness.AssertEventuallyAsync(
            () => mine.All(id => staying.LocalActors.Contains(id)),
            "the successor should have activated the actors it inherited", TimeSpan.FromSeconds(15));

        Assert.True(staying.LocalActors.Count >= before + mine.Length);
    }

    [Fact]
    public async Task ALimitOfZeroLeavesThemToActivateOnDemand()
    {
        await using var harness = new TestHarness();

        var leaving = await harness.NetworkedAsync("cold-a", seeds: [], configure: o => o.WarmHandoffLimit = 0);
        var staying = await harness.NetworkedAsync("cold-b", seeds: [$"127.0.0.1:{leaving.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => leaving.Cluster.Members.Count == 2 && staying.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        var mine = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"cold-{i}"))
            .Where(id => leaving.Cluster.OwnerOf(id) == "cold-a")
            .Take(5)
            .ToArray();

        foreach (var id in mine)
            await leaving.AskAsync<Total>(id, new GetTotal(), TimeSpan.FromSeconds(10));

        await leaving.StopAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Off is off. The addresses still work - the next message brings each actor back - which is
        // the behaviour this option turns the warming into.
        Assert.DoesNotContain(mine[0], staying.LocalActors);

        await staying.AskAsync<Total>(mine[0], new GetTotal(), TimeSpan.FromSeconds(15));
        Assert.Contains(mine[0], staying.LocalActors);
    }

    [Fact]
    public async Task AStandaloneNodeHasNobodyToHandOverTo()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        await system.AskAsync<Total>(ActorId.For<CounterActor>("alone"), new GetTotal(), TimeSpan.FromSeconds(10));

        // Stopping must not go looking for a successor that does not exist, or every single-node
        // shutdown would pay for a feature that only means something in a cluster.
        await system.StopAsync();
    }
}
