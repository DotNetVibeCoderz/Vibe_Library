// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ActorNet.Samples.Avalonia.ViewModels;

// Messages for the supervision demo.
[ActorMessage(Alias = "sup.count")] public sealed record Count(int By);
[ActorMessage(Alias = "sup.get")] public sealed record GetCount;
[ActorMessage(Alias = "sup.counted")] public sealed record Counted(int Value, int Restarts);
[ActorMessage(Alias = "sup.fail")] public sealed record Fail(string Why);

/// <summary>
/// Holds a running total in memory and fails on demand.
/// </summary>
/// <remarks>
/// State is deliberately not persisted: the whole point of the screen is that a restart discards
/// it, which is what makes the difference between Resume and Restart visible.
/// </remarks>
public class FragileCounterActor : ReceiveActor
{
    private int _total;

    public FragileCounterActor()
    {
        On<Count>(m => _total += m.By);
        On<Fail>(m => throw new InvalidOperationException(m.Why));
        On<GetCount>(async (_, ct) => await Context.ReplyAsync(new Counted(_total, Context.RestartCount), ct));
    }
}

/// <summary>
/// Supervision, made visible.
/// </summary>
/// <remarks>
/// Three actor types, identical except for the strategy they were registered with, so the same
/// failure produces three different outcomes side by side. That is a far clearer explanation of
/// Resume, Restart and Stop than any amount of prose about them.
/// </remarks>
public sealed partial class SupervisionViewModel : ScenarioViewModel
{
    private const string Key = "demo";

    [ObservableProperty] private string _resumeState = "-";
    [ObservableProperty] private string _restartState = "-";
    [ObservableProperty] private string _stopState = "-";
    [ObservableProperty] private string _budgetState = "-";

    public SupervisionViewModel(ActorSystem system)
        : base(system, "Supervision",
            "A failure is a supervised event, not a crash: the same exception can be resumed past, restarted through, or fatal - and the choice is configuration.")
    {
        // Same class, three registrations. The runtime keys its registry by actor type name, so
        // each of these is a distinct address space with its own policy.
        system.RegisterActor<ResumingCounter>(SupervisorStrategy.ResumeOnFailure);
        system.RegisterActor<RestartingCounter>(SupervisorStrategy.Default);
        system.RegisterActor<StoppingCounter>(SupervisorStrategy.StopOnFailure);
        system.RegisterActor<BudgetedCounter>(new OneForOneStrategy(static _ => Directive.Restart)
        {
            MaxRestarts = 3,
            Window = TimeSpan.FromSeconds(30),
        });

        system.RegisterMessage<Count>();
        system.RegisterMessage<GetCount>();
        system.RegisterMessage<Counted>();
        system.RegisterMessage<Fail>();
    }

    [RelayCommand]
    private Task CountAsync() => RunAsync(async () =>
    {
        foreach (var id in AllActors()) await System.TellAsync(id, new Count(10));
        Say("Sent +10 to all four. Each is holding its total in memory, with no persistence.");
        await Task.Delay(150);
    });

    [RelayCommand]
    private Task FailOnceAsync() => RunAsync(async () =>
    {
        foreach (var id in AllActors()) await System.TellAsync(id, new Fail("thrown on purpose"));
        Say("Threw once in each. Watch what happens to the totals:");
        await Task.Delay(300);

        Say("  Resume  - total kept. The bad message was dropped and the instance carried on.");
        Say("  Restart - total back to zero. A fresh instance, same address, same mailbox.");
        Say("  Stop    - deactivated. The next message activates a new one, so it also reads zero.");
    });

    [RelayCommand]
    private Task ExhaustBudgetAsync() => RunAsync(async () =>
    {
        Say("Sending 12 failures to the budgeted actor, which allows 3 restarts in 30s.");
        for (var i = 0; i < 12; i++)
            await System.TellAsync(ActorId.For<BudgetedCounter>(Key), new Fail($"failure {i + 1}"));

        await Task.Delay(600);

        var alive = System.LocalActors.Contains(ActorId.For<BudgetedCounter>(Key));
        Say(alive
            ? "It is still activated - the budget window has not expired yet."
            : "It was stopped rather than restarted again. That is the budget doing its job: without it, a poison message buys a fresh instance forever and burns a core.");
    });

    protected override async Task RefreshAsync()
    {
        ResumeState = await ReadAsync(ActorId.For<ResumingCounter>(Key));
        RestartState = await ReadAsync(ActorId.For<RestartingCounter>(Key));
        StopState = await ReadAsync(ActorId.For<StoppingCounter>(Key));
        BudgetState = await ReadAsync(ActorId.For<BudgetedCounter>(Key));
    }

    private async Task<string> ReadAsync(ActorId id)
    {
        try
        {
            var counted = await System.AskAsync<Counted>(id, new GetCount(), TimeSpan.FromSeconds(5));
            return counted.Restarts == 0
                ? $"total {counted.Value}"
                : $"total {counted.Value}, {counted.Restarts} restart(s)";
        }
        catch (AskTimeoutException)
        {
            return "not answering";
        }
    }

    private static ActorId[] AllActors() =>
    [
        ActorId.For<ResumingCounter>(Key),
        ActorId.For<RestartingCounter>(Key),
        ActorId.For<StoppingCounter>(Key),
        ActorId.For<BudgetedCounter>(Key),
    ];
}

/// <summary>Registered with Resume: keeps its state and drops the message that threw.</summary>
public sealed class ResumingCounter : FragileCounterActor;

/// <summary>Registered with the default strategy: rebuilt on failure, so its state is lost.</summary>
public sealed class RestartingCounter : FragileCounterActor;

/// <summary>Registered with Stop: a failure deactivates it.</summary>
public sealed class StoppingCounter : FragileCounterActor;

/// <summary>Registered with a restart budget: restarted, but only so many times.</summary>
public sealed class BudgetedCounter : FragileCounterActor;
