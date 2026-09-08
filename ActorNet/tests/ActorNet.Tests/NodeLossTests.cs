// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Client;
using ActorNet.Cluster;
using ActorNet.Serialization;

namespace ActorNet.Tests;

/// <summary>
/// Losing a node without warning, and carrying on.
/// </summary>
/// <remarks>
/// Every other cluster test stops a node politely, which announces a departure the peers act on at
/// once - and therefore proves nothing about failure detection, rebalancing under load, or whether
/// an actor's state can be picked up by whoever inherits its key. These pull the transport out from
/// under a node instead, which is the closest an in-process test gets to unplugging a cable.
/// </remarks>
public sealed class NodeLossTests
{
    /// <summary>Closes a node's transport without letting it say goodbye.</summary>
    private static async Task KillAsync(ActorSystem system)
    {
        Assert.NotNull(system.Transport);
        await system.Transport.StopAsync(CancellationToken.None);
        await system.Transport.DisposeAsync();
    }

    [Fact]
    public async Task AnActorIsNotHandedToAnOwnerNobodyCanReach()
    {
        await using var harness = new TestHarness();

        var holder = await harness.NetworkedAsync("hold-a", seeds: []);
        var doomed = await harness.NetworkedAsync("hold-b", seeds: [$"127.0.0.1:{holder.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => holder.Cluster.Members.Count == 2 && doomed.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        await TestHarness.AssertRingsAgreeAsync(holder, doomed);

        var theirs = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"hold-{i}"))
            .First(id => holder.Cluster.OwnerOf(id) == "hold-b");

        // Sent from one node to the other, stamped as coming from a member, which is what makes it
        // arrive already routed and therefore handled where it lands. That is a peer sending with a
        // stale view of the ring, and it is one of the ways an actor comes to be running on a node
        // the ring says does not own it - a warm handoff is the other.
        //
        // A client cannot be used for this any more: a node forwards a non-member's frame to the
        // owner precisely so this does not happen by accident.
        Assert.NotNull(doomed.Transport);

        var (alias, payload) = doomed.Serializer.Serialize(new Add(3));
        await doomed.Transport.SendAsync("hold-a", new WireEnvelope
        {
            Kind = WireKind.Message,
            Target = theirs.ToString(),
            MessageAlias = alias,
            Payload = payload,
            FromNode = "hold-b",
        }, TestContext.Current.CancellationToken);

        await TestHarness.AssertEventuallyAsync(() => holder.LocalActors.Contains(theirs),
            "a frame from a peer is handled where it arrives");

        await KillAsync(doomed);

        // Unreachable, not Down: still on the ring, still the owner, and nobody can talk to it.
        await TestHarness.AssertEventuallyAsync(
            () => holder.Cluster.Members.Single(m => m.NodeId == "hold-b").Status == MemberStatus.Unreachable,
            "the holder should notice the owner has gone quiet", TimeSpan.FromSeconds(15));

        // Rebalancing on that change used to deactivate this actor, handing it to a node nobody can
        // reach: gone here, unreachable there, held by nobody.
        Assert.Contains(theirs, holder.LocalActors);
    }

    [Fact]
    public async Task ASilentNodeIsEventuallyTakenOffTheRing()
    {
        await using var harness = new TestHarness();

        var survivor = await harness.NetworkedAsync("loss-a", seeds: []);
        var doomed = await harness.NetworkedAsync("loss-b", seeds: [$"127.0.0.1:{survivor.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => survivor.Cluster.Members.Count == 2 && doomed.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        await TestHarness.AssertRingsAgreeAsync(survivor, doomed);

        await KillAsync(doomed);

        // Unreachable first, because the usual cause of silence is a pause and moving a node's keys
        // costs a wave of deactivations. Only the longer deadline takes it off the ring.
        await TestHarness.AssertEventuallyAsync(
            () => survivor.Cluster.Members.Single(m => m.NodeId == "loss-b").Status == MemberStatus.Down,
            "the survivor should give up on a node that stopped answering", TimeSpan.FromSeconds(20));

        Assert.True(survivor.Cluster.IsSingleNode, "the survivor should own the whole ring again");
    }

    [Fact]
    public async Task TheKeysOfALostNodeAreInheritedWithTheirState()
    {
        await using var harness = new TestHarness();

        var survivor = await harness.NetworkedAsync("heir-a", seeds: []);
        var doomed = await harness.NetworkedAsync("heir-b", seeds: [$"127.0.0.1:{survivor.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => survivor.Cluster.Members.Count == 2 && doomed.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        await TestHarness.AssertRingsAgreeAsync(survivor, doomed);

        var theirs = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<WalletActor>($"heir-{i}"))
            .First(id => survivor.Cluster.OwnerOf(id) == "heir-b");

        await survivor.TellAsync(theirs, new Credit(250m));

        // The ask is the barrier that proves the credit was handled on the owner, and the wallet
        // flushes on deactivation - which the idle sweeper does long before the node dies.
        await survivor.AskAsync<Balance>(theirs, new GetBalance(), TimeSpan.FromSeconds(15));
        await doomed.DeactivateAsync(theirs);

        await KillAsync(doomed);

        await TestHarness.AssertEventuallyAsync(
            () => survivor.Cluster.IsSingleNode,
            "the ring should rebuild without the lost node", TimeSpan.FromSeconds(20));

        // The actor reactivates on its new owner and reads its state from the store. That is the
        // whole bet of deactivate-and-reload rather than migrating live state.
        var balance = await survivor.AskAsync<Balance>(theirs, new GetBalance(), TimeSpan.FromSeconds(15));
        Assert.Equal(250m, balance.Amount);
    }

    [Fact]
    public async Task ANodeThatComesBackIsPutBackOnTheRing()
    {
        await using var harness = new TestHarness();

        var survivor = await harness.NetworkedAsync("back-a", seeds: []);
        var flapping = await harness.NetworkedAsync("back-b", seeds: [$"127.0.0.1:{survivor.BoundPort}"]);
        var port = flapping.BoundPort;

        await TestHarness.AssertEventuallyAsync(
            () => survivor.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        await TestHarness.AssertRingsAgreeAsync(survivor, flapping);

        await KillAsync(flapping);

        await TestHarness.AssertEventuallyAsync(
            () => survivor.Cluster.Members.Single(m => m.NodeId == "back-b").Status == MemberStatus.Down,
            "the survivor should give up on it", TimeSpan.FromSeconds(20));

        // A new node at the same address, as a restart would be. It has to be able to rejoin: a
        // peer that remembers the old one as down and never gossips to it again is exactly the
        // failure the seed retry exists to break.
        var replacement = await harness.NetworkedAsync("back-b2", seeds: [$"127.0.0.1:{survivor.BoundPort}"],
            configure: o => o.Port = port);

        await TestHarness.AssertEventuallyAsync(
            () => survivor.Cluster.Members.Any(m => m.NodeId == "back-b2" && m.Status == MemberStatus.Up)
                  && replacement.Cluster.Members.Any(m => m.NodeId == "back-a" && m.Status == MemberStatus.Up),
            "a replacement at the same address should join", TimeSpan.FromSeconds(20));
    }
}
