// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Tests;

public sealed class SupervisionTests
{
    [Fact]
    public async Task RestartRebuildsTheActorAndDiscardsItsState()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<FlakyActor>(SupervisorStrategy.Default);

        var flaky = system.ActorOf<FlakyActor>($"restart-{Guid.NewGuid():N}");
        await flaky.TellAsync(new Add(5));
        Assert.Equal(5, (await flaky.AskAsync<Total>(new GetTotal())).Value);

        await flaky.TellAsync(new Boom("expected"));

        // The restart is what makes this zero: a fresh instance, same address, same mailbox.
        await TestHarness.AssertEventuallyAsync(
            () => flaky.AskAsync<Total>(new GetTotal()).GetAwaiter().GetResult().Value == 0,
            "the actor should have been restarted with fresh state");
    }

    [Fact]
    public async Task TheActorKeepsWorkingAfterARestart()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<FlakyActor>(SupervisorStrategy.Default);

        var flaky = system.ActorOf<FlakyActor>($"survives-{Guid.NewGuid():N}");
        await flaky.TellAsync(new Boom("first"));
        await flaky.TellAsync(new Add(3));

        var total = await flaky.AskAsync<Total>(new GetTotal(), TimeSpan.FromSeconds(10));
        Assert.Equal(3, total.Value);
    }

    [Fact]
    public async Task ResumeKeepsStateAndJustDropsTheBadMessage()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<FlakyActor>(SupervisorStrategy.ResumeOnFailure);

        var flaky = system.ActorOf<FlakyActor>($"resume-{Guid.NewGuid():N}");
        await flaky.TellAsync(new Add(9));
        await flaky.TellAsync(new Boom("ignored"));

        var total = await flaky.AskAsync<Total>(new GetTotal(), TimeSpan.FromSeconds(10));
        Assert.Equal(9, total.Value);
    }

    [Fact]
    public async Task StopOnFailureDeactivatesTheActor()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<FlakyActor>(SupervisorStrategy.StopOnFailure);

        var id = ActorId.For<FlakyActor>($"stop-{Guid.NewGuid():N}");
        await system.TellAsync(id, new Add(1));
        await TestHarness.AssertEventuallyAsync(() => system.LocalActors.Contains(id), "the actor should be active");

        await system.TellAsync(id, new Boom("fatal"));

        await TestHarness.AssertEventuallyAsync(() => !system.LocalActors.Contains(id),
            "a stop directive should have deactivated the actor");
    }

    [Fact]
    public async Task TheRestartBudgetStopsAPoisonMessageFromSpinningForever()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        // Three restarts a minute, then give up. Without a budget, the twenty failures below
        // would each buy a fresh instance and the actor would never stop burning a core.
        system.RegisterActor<FlakyActor>(new OneForOneStrategy(static _ => Directive.Restart)
        {
            MaxRestarts = 3,
            Window = TimeSpan.FromMinutes(1),
        });

        var id = ActorId.For<FlakyActor>($"budget-{Guid.NewGuid():N}");
        for (var i = 0; i < 20; i++) await system.TellAsync(id, new Boom($"failure-{i}"));

        await TestHarness.AssertEventuallyAsync(() => !system.LocalActors.Contains(id),
            "the actor should have been stopped once it exceeded its restart budget");

        var restarts = system.Metrics.Snapshot(includeActors: false).Restarts;
        Assert.InRange(restarts, 1, 4);
    }

    [Fact]
    public async Task ADefaultStrategyStopsRatherThanRestartsOnAWiringBug()
    {
        // Restarting an actor whose address cannot be resolved just fails identically forever,
        // so the default deliberately treats that class of failure as fatal.
        Assert.Equal(Directive.Stop, SupervisorStrategy.Default.Decide(new ActorTypeNotRegisteredException("Ghost")));
        Assert.Equal(Directive.Stop, SupervisorStrategy.Default.Decide(new FormatException()));
        Assert.Equal(Directive.Restart, SupervisorStrategy.Default.Decide(new InvalidOperationException()));
        Assert.Equal(Directive.Restart, SupervisorStrategy.Default.Decide(new TimeoutException()));
    }

    [Fact]
    public async Task AFailedAskIsAnsweredWithTheFailureInsteadOfATimeout()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<AskingFlakyActor>();

        var actor = system.ActorOf<AskingFlakyActor>("failing-ask");

        // A caller that gets an exception knows what went wrong. A caller that gets a timeout
        // just knows it waited, which is the harder bug to chase.
        var ex = await Assert.ThrowsAsync<ActorNetException>(
            async () => await actor.AskAsync<Total>(new Boom("handler failed"), TimeSpan.FromSeconds(5)));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("handler failed", ex.InnerException!.Message);
    }

    [Fact]
    public async Task ChildrenAreSupervisedAndStoppedWithTheirParent()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        var parent = system.ActorOf<ParentActor>($"tree-{Guid.NewGuid():N}");
        var childKey = $"child-{Guid.NewGuid():N}";

        await parent.TellAsync(new SpawnA(childKey));
        await TestHarness.AssertEventuallyAsync(
            () => system.LocalActors.Contains(ActorId.For<CounterActor>(childKey)),
            "the child should have been spawned");

        await system.DeactivateAsync(parent.Id);

        await TestHarness.AssertEventuallyAsync(
            () => !system.LocalActors.Contains(ActorId.For<CounterActor>(childKey)),
            "stopping a parent should stop its children");
    }

    [Fact]
    public async Task EscalationStopsTheChildAndReachesTheParentsStrategy()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<EscalatingActor>(new OneForOneStrategy(static _ => Directive.Escalate));

        var id = ActorId.For<EscalatingActor>($"escalate-{Guid.NewGuid():N}");
        await system.TellAsync(id, new Boom("upwards"));

        // At the root there is no parent, so escalation ends with the actor stopped and the
        // failure logged rather than silently resumed.
        await TestHarness.AssertEventuallyAsync(() => !system.LocalActors.Contains(id),
            "an escalated failure with no parent should stop the actor");
    }
}

/// <summary>Throws from a handler that a caller is asking on.</summary>
public sealed class AskingFlakyActor : ReceiveActor
{
    public AskingFlakyActor() => On<Boom>(m => throw new InvalidOperationException(m.Message));
}

/// <summary>Fails so that an escalating strategy has something to escalate.</summary>
public sealed class EscalatingActor : ReceiveActor
{
    public EscalatingActor() => On<Boom>(m => throw new InvalidOperationException(m.Message));
}
