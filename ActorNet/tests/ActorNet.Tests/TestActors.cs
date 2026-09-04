// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using ActorNet.Persistence;

namespace ActorNet.Tests;

// Messages shared across the suite.
public sealed record Ping(int Value);
public sealed record Pong(int Value);
public sealed record Add(int By);
public sealed record GetTotal;
public sealed record Total(int Value);
public sealed record Boom(string Message);
public sealed record SpawnA(string Key);
public sealed record TellChild(string Key, int By);

/// <summary>Counts what it is told and answers questions about it.</summary>
public sealed class CounterActor : ReceiveActor
{
    private int _total;

    /// <summary>Every activation this test run has seen, so tests can assert on lifecycle.</summary>
    public static readonly ConcurrentBag<string> Activations = [];

    public static readonly ConcurrentBag<string> Deactivations = [];

    public CounterActor()
    {
        On<Add>(m => _total += m.By);
        On<GetTotal>(async (_, ct) => await Context.ReplyAsync(new Total(_total), ct));
        On<Ping>(async (m, ct) => await Context.ReplyAsync(new Pong(m.Value), ct));
    }

    protected override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        Activations.Add(Context.Self.ToString());
        return Task.CompletedTask;
    }

    protected override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        Deactivations.Add($"{Context.Self}:{reason}");
        return Task.CompletedTask;
    }
}

/// <summary>Throws on demand, so supervision can be observed.</summary>
public sealed class FlakyActor : ReceiveActor
{
    /// <summary>Messages handled successfully, per actor key, across restarts.</summary>
    public static readonly ConcurrentDictionary<string, int> Handled = new();

    /// <summary>Restarts observed, per actor key.</summary>
    public static readonly ConcurrentDictionary<string, int> Restarts = new();

    private int _sinceRestart;

    public FlakyActor()
    {
        On<Boom>(m => throw new InvalidOperationException(m.Message));
        On<Add>(m =>
        {
            _sinceRestart += m.By;
            Handled.AddOrUpdate(Context.Self.Key, 1, static (_, v) => v + 1);
        });
        On<GetTotal>(async (_, ct) => await Context.ReplyAsync(new Total(_sinceRestart), ct));
    }

    protected override Task OnRestartAsync(Exception cause, CancellationToken cancellationToken)
    {
        Restarts.AddOrUpdate(Context.Self.Key, 1, static (_, v) => v + 1);
        return Task.CompletedTask;
    }
}

/// <summary>Never answers, so ask timeouts can be observed.</summary>
public sealed class SilentActor : ReceiveActor
{
    public SilentActor() => On<Ping>(static _ => { });
}

/// <summary>Spawns children and forwards to them, so supervision trees can be observed.</summary>
public sealed class ParentActor : ReceiveActor
{
    public ParentActor()
    {
        On<SpawnA>(m => Context.SpawnChild<CounterActor>(m.Key));
        On<TellChild>(async (m, ct) => await Context.TellAsync(ActorId.For<CounterActor>(m.Key), new Add(m.By), ct));
    }
}

/// <summary>State that survives deactivation.</summary>
public sealed class WalletState
{
    public decimal Balance { get; set; }
    public int Operations { get; set; }
}

public sealed record Credit(decimal Amount);
public sealed record GetBalance;
public sealed record Balance(decimal Amount, int Operations);

/// <summary>A persistent actor, to check that state reloads on reactivation.</summary>
public sealed class WalletActor : PersistentActor<WalletState>
{
    protected override Task ReceiveAsync(object message, CancellationToken cancellationToken) => message switch
    {
        Credit credit => Handle(credit),
        GetBalance => Context.ReplyAsync(new Balance(State.Balance, State.Operations), cancellationToken).AsTask(),
        _ => Task.CompletedTask,
    };

    private Task Handle(Credit credit)
    {
        State.Balance += credit.Amount;
        State.Operations++;
        return Task.CompletedTask;
    }
}

// Event-sourced ledger.
public sealed record Deposited(decimal Amount);
public sealed record Withdrawn(decimal Amount);
public sealed record Deposit(decimal Amount);
public sealed record Withdraw(decimal Amount);
public sealed record Rejected(string Reason);

public sealed class LedgerState
{
    public decimal Balance { get; set; }
    public int EventCount { get; set; }
}

/// <summary>An event-sourced actor, to check that a fold over history reconstructs state.</summary>
public sealed class LedgerActor : EventSourcedActor<LedgerState>
{
    protected override void Apply(object domainEvent)
    {
        switch (domainEvent)
        {
            case Deposited d:
                State.Balance += d.Amount;
                State.EventCount++;
                break;
            case Withdrawn w:
                State.Balance -= w.Amount;
                State.EventCount++;
                break;
        }
    }

    protected override async Task ReceiveAsync(object message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case Deposit d:
                await PersistAsync(new Deposited(d.Amount), cancellationToken);
                break;

            case Withdraw w when State.Balance >= w.Amount:
                await PersistAsync(new Withdrawn(w.Amount), cancellationToken);
                break;

            case Withdraw:
                // The command is rejected, so nothing is written: a refused withdrawal is not
                // something that happened.
                await Context.ReplyAsync(new Rejected("insufficient funds"), cancellationToken);
                break;

            case GetBalance:
                await Context.ReplyAsync(new Balance(State.Balance, State.EventCount), cancellationToken);
                break;
        }
    }
}
