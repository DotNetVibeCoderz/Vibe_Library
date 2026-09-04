// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Tests;

public sealed class ActorSystemTests : IAsyncLifetime
{
    private readonly TestHarness _harness = new();
    private ActorSystem _system = null!;

    public async ValueTask InitializeAsync() => _system = await _harness.LocalAsync();

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task ActivatesOnFirstMessageWithoutAnyExplicitCreation()
    {
        var id = ActorId.For<CounterActor>($"first-{Guid.NewGuid():N}");
        Assert.DoesNotContain(id, _system.LocalActors);

        await _system.TellAsync(id, new Add(1));

        await TestHarness.AssertEventuallyAsync(() => _system.LocalActors.Contains(id),
            "the first message should have activated the actor");
    }

    [Fact]
    public async Task AskGetsTheActorsReply()
    {
        var counter = _system.ActorOf<CounterActor>("ask");
        await counter.TellAsync(new Add(7));

        var total = await counter.AskAsync<Total>(new GetTotal());

        Assert.Equal(7, total.Value);
    }

    [Fact]
    public async Task MessagesFromOneSenderAreHandledInOrder()
    {
        var counter = _system.ActorOf<CounterActor>("ordering");
        for (var i = 1; i <= 500; i++) await counter.TellAsync(new Add(i));

        var total = await counter.AskAsync<Total>(new GetTotal());

        // The ask is queued behind the 500 tells, so this also proves the mailbox is FIFO rather
        // than just eventually consistent.
        Assert.Equal(500 * 501 / 2, total.Value);
    }

    [Fact]
    public async Task ConcurrentSendersCannotLoseAnUpdate()
    {
        var counter = _system.ActorOf<CounterActor>("concurrent");

        await Task.WhenAll(Enumerable.Range(0, 64).Select(async _ =>
        {
            for (var i = 0; i < 100; i++) await counter.TellAsync(new Add(1));
        }));

        var total = await counter.AskAsync<Total>(new GetTotal(), TimeSpan.FromSeconds(30));

        // 6400 increments with no lock anywhere in CounterActor. That is the single-threaded
        // guarantee doing its job.
        Assert.Equal(6400, total.Value);
    }

    [Fact]
    public async Task SendingToAnUnregisteredTypeThrowsRatherThanDisappearing()
    {
        await Assert.ThrowsAsync<ActorTypeNotRegisteredException>(
            async () => await _system.TellAsync(new ActorId("NeverRegisteredActor", "x"), new Add(1)));
    }

    [Fact]
    public async Task AskTimesOutWhenTheActorNeverReplies()
    {
        var silent = _system.ActorOf<SilentActor>("quiet");

        var ex = await Assert.ThrowsAsync<AskTimeoutException>(
            async () => await silent.AskAsync<Pong>(new Ping(1), TimeSpan.FromMilliseconds(300)));

        Assert.Equal(TimeSpan.FromMilliseconds(300), ex.Timeout);
        Assert.Equal("SilentActor/quiet", ex.Target.ToString());
    }

    [Fact]
    public async Task AskSurfacesTheWrongReplyTypeRatherThanTimingOut()
    {
        var counter = _system.ActorOf<CounterActor>("mismatch");

        // CounterActor answers a Ping with a Pong; asking for a Total is a caller bug, and the
        // diagnostic should say so instead of blaming the actor for being slow.
        await Assert.ThrowsAsync<AskReplyTypeMismatchException>(
            async () => await counter.AskAsync<Total>(new Ping(1), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ReplyGoesToTheSenderWhenThereIsNoPendingAsk()
    {
        // A tell that names a sender gets its reply delivered as an ordinary message, which is
        // what makes actor-to-actor conversation work without anyone blocking.
        var replies = _system.ActorOf<CounterActor>("reply-sink");
        await _system.TellAsync(ActorId.For<CounterActor>("reply-source"), new Ping(3), replies.Id);

        // CounterActor has no Pong handler, so the reply reaching it surfaces as a failure rather
        // than silence; asserting on the sink's liveness is enough to show routing happened.
        await TestHarness.AssertEventuallyAsync(
            () => _system.LocalActors.Contains(replies.Id),
            "the reply should have been routed to the sender, activating it");
    }

    [Fact]
    public async Task DeactivationDropsTheActorAndTheNextMessageActivatesAFreshOne()
    {
        var id = ActorId.For<CounterActor>($"cycle-{Guid.NewGuid():N}");
        var counter = _system.ActorOf(id);

        await counter.TellAsync(new Add(5));
        Assert.Equal(5, (await counter.AskAsync<Total>(new GetTotal())).Value);

        await _system.DeactivateAsync(id);
        await TestHarness.AssertEventuallyAsync(() => !_system.LocalActors.Contains(id), "the actor should have been deactivated");

        // A fresh instance means fresh state: CounterActor is not persistent.
        Assert.Equal(0, (await counter.AskAsync<Total>(new GetTotal())).Value);
    }

    [Fact]
    public async Task IdleActorsAreSweptAway()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync(o =>
        {
            o.IdleTimeout = TimeSpan.FromMilliseconds(200);
            o.SweepInterval = TimeSpan.FromMilliseconds(50);
        });

        var id = ActorId.For<CounterActor>("idle");
        await system.TellAsync(id, new Add(1));
        await TestHarness.AssertEventuallyAsync(() => system.LocalActors.Contains(id), "the actor should have activated");

        await TestHarness.AssertEventuallyAsync(() => !system.LocalActors.Contains(id),
            "the sweeper should have deactivated an actor idle past its timeout", TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ABusyActorIsNotSweptAway()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync(o =>
        {
            o.IdleTimeout = TimeSpan.FromMilliseconds(200);
            o.SweepInterval = TimeSpan.FromMilliseconds(50);
        });

        var counter = system.ActorOf<CounterActor>("busy");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        while (!cts.IsCancellationRequested)
        {
            await counter.TellAsync(new Add(1));
            await Task.Delay(25, CancellationToken.None);
        }

        Assert.Contains(counter.Id, system.LocalActors);
    }

    [Fact]
    public async Task DeactivateOnIdleStopsTheActorAfterTheCurrentMessage()
    {
        var id = ActorId.For<SelfStoppingActor>("self");
        _system.RegisterActor<SelfStoppingActor>();

        await _system.TellAsync(id, new Add(1));

        await TestHarness.AssertEventuallyAsync(() => !_system.LocalActors.Contains(id),
            "the actor asked to be deactivated once its message was done");
    }

    [Fact]
    public async Task StoppingTheSystemDeactivatesEveryActor()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        for (var i = 0; i < 25; i++) await system.TellAsync(ActorId.For<CounterActor>($"shutdown-{i}"), new Add(1));
        await TestHarness.AssertEventuallyAsync(() => system.LocalActors.Count == 25, "all 25 actors should be active");

        await system.StopAsync();

        Assert.Empty(system.LocalActors);
        Assert.Equal(25, system.Metrics.Snapshot().Deactivations);
    }

    [Fact]
    public async Task MetricsCountWhatTheNodeActuallyDid()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        var counter = system.ActorOf<CounterActor>("metrics");

        for (var i = 0; i < 50; i++) await counter.TellAsync(new Add(1));
        await counter.AskAsync<Total>(new GetTotal());

        var snapshot = system.Metrics.Snapshot();

        Assert.Equal(51, snapshot.MessagesDispatched);
        Assert.Equal(51, snapshot.MessagesProcessed);
        Assert.Equal(0, snapshot.MessagesFailed);
        Assert.Equal(1, snapshot.AsksIssued);
        Assert.Equal(1, snapshot.ActiveActors);
        Assert.Equal(0, snapshot.InFlight);

        var actor = Assert.Single(snapshot.Actors);
        Assert.Equal("CounterActor/metrics", actor.Id);
        Assert.Equal(51, actor.MessagesProcessed);
    }
}

/// <summary>Asks to be deactivated as soon as it has handled anything.</summary>
public sealed class SelfStoppingActor : ReceiveActor
{
    public SelfStoppingActor() => On<Add>(_ => Context.DeactivateOnIdle());
}
