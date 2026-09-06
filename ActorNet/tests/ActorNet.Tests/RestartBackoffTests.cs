// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics;

namespace ActorNet.Tests;

/// <summary>
/// Slowing down an actor that keeps failing.
/// </summary>
/// <remarks>
/// Restarting was immediate, so an actor whose cause has not gone away burned through its whole
/// budget as fast as its mailbox could feed it - and every restart of a database-backed actor is
/// another connection attempt at whatever is already struggling. The restart budget bounded the
/// loop; it did not slow it down.
/// </remarks>
public sealed class RestartBackoffTests
{
    private static SupervisorStrategy Strategy(int min, int max, double jitter = 0) =>
        new OneForOneStrategy(_ => Directive.Restart)
        {
            MinBackoff = TimeSpan.FromMilliseconds(min),
            MaxBackoff = TimeSpan.FromMilliseconds(max),
            BackoffJitter = jitter,
        };

    [Fact]
    public void TheFirstRestartDoesNotWait()
    {
        // The common failure is a one-off - a timeout, a bad payload - and making every actor pay
        // for the rare crash loop would be the wrong trade.
        Assert.Equal(TimeSpan.Zero, Strategy(100, 5_000).BackoffFor(1));
    }

    [Fact]
    public void TheWaitDoublesFromTheSecondRestart()
    {
        var strategy = Strategy(100, 5_000);

        Assert.Equal(TimeSpan.FromMilliseconds(100), strategy.BackoffFor(2));
        Assert.Equal(TimeSpan.FromMilliseconds(200), strategy.BackoffFor(3));
        Assert.Equal(TimeSpan.FromMilliseconds(400), strategy.BackoffFor(4));
        Assert.Equal(TimeSpan.FromMilliseconds(800), strategy.BackoffFor(5));
    }

    [Fact]
    public void TheWaitStopsAtTheCeiling()
    {
        var strategy = Strategy(100, 1_000);

        Assert.Equal(TimeSpan.FromMilliseconds(1_000), strategy.BackoffFor(10));

        // Far enough out that a naive doubling would have overflowed rather than saturated.
        Assert.Equal(TimeSpan.FromMilliseconds(1_000), strategy.BackoffFor(500));
    }

    [Fact]
    public void ABackoffOfZeroKeepsRestartsImmediate()
    {
        var strategy = Strategy(0, 5_000);

        Assert.Equal(TimeSpan.Zero, strategy.BackoffFor(5));
    }

    [Fact]
    public void JitterSpreadsTheWaitWithoutLeavingItsBand()
    {
        var strategy = Strategy(1_000, 10_000, jitter: 0.2);

        var waits = Enumerable.Range(0, 200).Select(_ => strategy.BackoffFor(2).TotalMilliseconds).ToArray();

        // A dependency going down fails every actor that touches it in the same millisecond; without
        // jitter they would all come back together and hit it again in lockstep.
        Assert.True(waits.Distinct().Count() > 100, "the wait should be spread, not fixed");
        Assert.All(waits, w => Assert.InRange(w, 800, 1_200));
    }

    [Fact]
    public void TheDefaultCeilingLeavesRoomInsideTheWindow()
    {
        var strategy = SupervisorStrategy.Default;

        // Ten restarts have to fit inside the window with time to spare. If the waits added up to
        // more than it, the window would keep resetting and a permanently broken actor would
        // restart forever instead of hitting the budget and stopping.
        var total = Enumerable.Range(1, strategy.MaxRestarts).Sum(i => strategy.BackoffFor(i).TotalMilliseconds);

        Assert.True(total < strategy.Window.TotalMilliseconds * 0.75,
            $"{strategy.MaxRestarts} restarts wait {total}ms in total, against a {strategy.Window} window");
    }

    [Fact]
    public async Task ARepeatedlyFailingActorIsSlowedDown()
    {
        await using var harness = new TestHarness();

        var system = await harness.LocalAsync();
        system.RegisterActor<FlakyActor>(Strategy(200, 2_000));

        var id = ActorId.For<FlakyActor>("backoff-loop");
        var clock = Stopwatch.StartNew();

        for (var i = 0; i < 4; i++) await system.TellAsync(id, new Boom("again"));

        // Four failures: the first restart is immediate, then 200ms and 400ms, so the fourth
        // failure cannot have been reached before 600ms have passed.
        await TestHarness.AssertEventuallyAsync(
            () => FlakyActor.Restarts.TryGetValue("backoff-loop", out var n) && n >= 3,
            "the actor should keep failing and restarting", TimeSpan.FromSeconds(10));

        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(600),
            $"three restarts took {clock.Elapsed}, which is faster than the backoff allows");
    }
}
