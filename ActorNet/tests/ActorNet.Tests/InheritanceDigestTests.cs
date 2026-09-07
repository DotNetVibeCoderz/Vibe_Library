// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Tests;

/// <summary>
/// Warming the keys of a node that was lost without warning.
/// </summary>
/// <remarks>
/// The warm handoff only covers a node that leaves politely, because only that node knows what it
/// was holding. A node killed outright takes that knowledge with it, and the survivors inherit a set
/// of keys with no idea which were live - so every one of them pays a read at the moment traffic
/// arrives, which is exactly when the cluster is already one node short. Telling each successor in
/// advance is the only way to know, and it is off by default because it is traffic a healthy cluster
/// pays all the time.
/// </remarks>
public sealed class InheritanceDigestTests
{
    private static void Digesting(ActorSystemOptions options)
    {
        options.InheritanceDigestLimit = 100;
        options.InheritanceDigestInterval = TimeSpan.FromMilliseconds(300);

        // The actors here are idle by construction, and an idle sweep would deactivate the very
        // thing being warmed.
        options.IdleTimeout = TimeSpan.FromMinutes(10);
    }

    /// <summary>Closes a node's transport without letting it say goodbye.</summary>
    private static async Task KillAsync(ActorSystem system)
    {
        Assert.NotNull(system.Transport);
        await system.Transport.StopAsync(CancellationToken.None);
        await system.Transport.DisposeAsync();
    }

    [Fact]
    public async Task TheSurvivorActivatesWhatTheLostNodeWasHolding()
    {
        await using var harness = new TestHarness();

        var survivor = await harness.NetworkedAsync("dig-a", seeds: [], configure: Digesting);
        var doomed = await harness.NetworkedAsync("dig-b", seeds: [$"127.0.0.1:{survivor.BoundPort}"], configure: Digesting);

        await TestHarness.AssertEventuallyAsync(
            () => survivor.Cluster.Members.Count == 2 && doomed.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        await TestHarness.AssertRingsAgreeAsync(survivor, doomed);

        var theirs = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"dig-{i}"))
            .Where(id => survivor.Cluster.OwnerOf(id) == "dig-b")
            .Take(4)
            .ToArray();

        Assert.NotEmpty(theirs);

        foreach (var id in theirs)
            await survivor.AskAsync<Total>(id, new GetTotal(), TimeSpan.FromSeconds(15));

        // They are running on the node that is about to die, not here.
        foreach (var id in theirs) Assert.DoesNotContain(id, survivor.LocalActors);

        // Wait for a digest to have been sent and taken in. Without one, the survivor inherits the
        // keys and has no idea any of them were live.
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        await KillAsync(doomed);

        await TestHarness.AssertEventuallyAsync(
            () => theirs.All(id => survivor.LocalActors.Contains(id)),
            () => "the survivor should have activated the keys it inherited; it was missing " +
                  string.Join(", ", theirs.Where(id => !survivor.LocalActors.Contains(id))),
            TimeSpan.FromSeconds(25));
    }

    [Fact]
    public async Task NothingIsSentWhenTheDigestIsOff()
    {
        await using var harness = new TestHarness();

        // The default. A digest is bandwidth a healthy cluster pays continuously, so it is opt-in,
        // and the behaviour without it is what the warm handoff and on-demand activation give.
        var survivor = await harness.NetworkedAsync("off-a", seeds: []);
        var doomed = await harness.NetworkedAsync("off-b", seeds: [$"127.0.0.1:{survivor.BoundPort}"]);

        Assert.Equal(0, survivor.Options.InheritanceDigestLimit);

        await TestHarness.AssertEventuallyAsync(
            () => survivor.Cluster.Members.Count == 2 && doomed.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        await TestHarness.AssertRingsAgreeAsync(survivor, doomed);

        var theirs = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"off-{i}"))
            .First(id => survivor.Cluster.OwnerOf(id) == "off-b");

        await survivor.AskAsync<Total>(theirs, new GetTotal(), TimeSpan.FromSeconds(15));

        await KillAsync(doomed);

        await TestHarness.AssertEventuallyAsync(
            () => survivor.Cluster.IsSingleNode,
            "the ring should rebuild without the lost node", TimeSpan.FromSeconds(20));

        // Still addressable - the next message brings it back - which is exactly the behaviour the
        // digest is an optimisation of, not a replacement for.
        Assert.DoesNotContain(theirs, survivor.LocalActors);
        await survivor.AskAsync<Total>(theirs, new GetTotal(), TimeSpan.FromSeconds(15));
        Assert.Contains(theirs, survivor.LocalActors);
    }

    [Fact]
    public void ANegativeLimitIsRefused()
    {
        var options = new ActorSystemOptions { NodeId = "bad", InheritanceDigestLimit = -1 };

        // Zero is off. A negative number is somebody expecting it to mean something else.
        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void AnIntervalOfZeroIsRefusedWhenTheDigestIsOn()
    {
        var options = new ActorSystemOptions
        {
            NodeId = "bad",
            InheritanceDigestLimit = 10,
            InheritanceDigestInterval = TimeSpan.Zero,
        };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);

        // Off, so the interval never runs and is nobody's problem.
        options.InheritanceDigestLimit = 0;
        options.Validate();
    }
}
