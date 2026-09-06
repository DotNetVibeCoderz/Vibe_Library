// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;

namespace ActorNet.Tests;

/// <summary>
/// Being told when another actor stops.
/// </summary>
/// <remarks>
/// The interesting question here is not the plumbing but which stops count. A virtual actor idles
/// out, moves between nodes and goes down with its host as a matter of course, and its address
/// stays valid through all of it. Reporting those would train a watcher to ignore the notification,
/// which would make the two that mean something useless as well.
/// </remarks>
public sealed class WatchTests
{
    [Fact]
    public async Task AWatcherHearsWhenTheWatchedActorIsStopped()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<WatcherActor>();

        var watcher = ActorId.For<WatcherActor>("w1");
        var target = ActorId.For<CounterActor>("watched-1");

        await system.TellAsync(watcher, new WatchThis(target.ToString()));
        await system.AskAsync<Total>(target, new GetTotal(), TimeSpan.FromSeconds(5));

        await system.DeactivateAsync(target);

        await TestHarness.AssertEventuallyAsync(
            () => WatcherActor.Seen.Contains(target.ToString()),
            "the watcher should have been told", TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ASupervisorGivingUpIsATermination()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<WatcherActor>();
        system.RegisterActor<FlakyActor>(SupervisorStrategy.StopOnFailure);

        var watcher = ActorId.For<WatcherActor>("w2");
        var doomed = ActorId.For<FlakyActor>("watched-doomed");

        await system.TellAsync(watcher, new WatchThis(doomed.ToString()));
        await system.TellAsync(doomed, new Boom("stopped for good"));

        await TestHarness.AssertEventuallyAsync(
            () => WatcherActor.Seen.Contains(doomed.ToString()),
            "a supervisor stopping an actor should reach its watcher", TimeSpan.FromSeconds(10));

        Assert.Equal(DeactivationReason.Supervision, WatcherActor.Reasons[doomed.ToString()]);
    }

    [Fact]
    public async Task IdlingOutIsNotATermination()
    {
        await using var harness = new TestHarness();

        // A short idle timeout, so the sweeper deactivates the target during the test.
        var system = await harness.LocalAsync(o =>
        {
            o.IdleTimeout = TimeSpan.FromMilliseconds(200);
            o.SweepInterval = TimeSpan.FromMilliseconds(100);
        });

        system.RegisterActor<WatcherActor>();

        var watcher = ActorId.For<WatcherActor>("w3");
        var target = ActorId.For<CounterActor>("watched-idle");

        await system.TellAsync(watcher, new WatchThis(target.ToString()));
        await system.AskAsync<Total>(target, new GetTotal(), TimeSpan.FromSeconds(5));

        await TestHarness.AssertEventuallyAsync(
            () => CounterActor.Deactivations.Contains($"{target}:{DeactivationReason.Idle}"),
            "the target should have idled out", TimeSpan.FromSeconds(10));

        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // The address is still good and the next message brings it back, so there is nothing to
        // report. A watcher told about this would learn to ignore the notification entirely.
        Assert.DoesNotContain(target.ToString(), WatcherActor.Seen);
    }

    [Fact]
    public async Task AWithdrawnWatchIsNotHonoured()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<WatcherActor>();

        var watcher = ActorId.For<WatcherActor>("w4");
        var target = ActorId.For<CounterActor>("watched-unwatched");

        await system.TellAsync(watcher, new WatchThis(target.ToString()));
        await system.AskAsync<Total>(target, new GetTotal(), TimeSpan.FromSeconds(5));
        await system.TellAsync(watcher, new UnwatchThis(target.ToString()));

        // Two barriers, because the unwatch takes two hops. Asking the watcher proves it has
        // handled UnwatchThis and therefore sent the Unwatch; asking the target proves the target
        // has handled that in turn. Everything queued before an ask is done when its reply lands.
        await system.AskAsync<Total>(watcher, new GetTotal(), TimeSpan.FromSeconds(5));
        await system.AskAsync<Total>(target, new GetTotal(), TimeSpan.FromSeconds(5));

        await system.DeactivateAsync(target);
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        Assert.DoesNotContain(target.ToString(), WatcherActor.Seen);
    }

    [Fact]
    public async Task AWatchCrossesANodeBoundary()
    {
        await using var harness = new TestHarness();

        var here = await harness.NetworkedAsync("watch-a", seeds: []);
        var there = await harness.NetworkedAsync("watch-b", seeds: [$"127.0.0.1:{here.BoundPort}"]);

        foreach (var node in new[] { here, there }) node.RegisterActor<WatcherActor>();

        await TestHarness.AssertEventuallyAsync(
            () => here.Cluster.Members.Count == 2 && there.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        var watcher = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<WatcherActor>($"wx-{i}"))
            .First(id => here.Cluster.OwnerOf(id) == "watch-a");

        var target = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"tx-{i}"))
            .First(id => here.Cluster.OwnerOf(id) == "watch-b");

        await here.TellAsync(watcher, new WatchThis(target.ToString()));
        await here.AskAsync<Total>(target, new GetTotal(), TimeSpan.FromSeconds(15));

        await there.DeactivateAsync(target);

        // The watch is registered on the node that owns the target and the notice comes back over
        // the wire, which is the half a local-only implementation would quietly get wrong.
        await TestHarness.AssertEventuallyAsync(
            () => WatcherActor.Seen.Contains(target.ToString()),
            "a watch should survive the trip to another node", TimeSpan.FromSeconds(20));
    }
}

public sealed record WatchThis(string Target);
public sealed record UnwatchThis(string Target);

/// <summary>Records every <see cref="Terminated"/> it is sent.</summary>
public sealed class WatcherActor : ReceiveActor
{
    public static readonly ConcurrentBag<string> Seen = [];
    public static readonly ConcurrentDictionary<string, DeactivationReason> Reasons = new();

    public WatcherActor()
    {
        On<WatchThis>(async (m, ct) => await Context.WatchAsync(ActorId.Parse(m.Target), ct));
        On<UnwatchThis>(async (m, ct) => await Context.UnwatchAsync(ActorId.Parse(m.Target), ct));
        On<Terminated>(m =>
        {
            Seen.Add(m.Actor);
            Reasons[m.Actor] = m.Reason;
        });

        // A barrier a test can await: the reply only comes back once everything queued ahead of it
        // has been handled.
        On<GetTotal>(async (_, ct) => await Context.ReplyAsync(new Total(Seen.Count), ct));
    }
}
