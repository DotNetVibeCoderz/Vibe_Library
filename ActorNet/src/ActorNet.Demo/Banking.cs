// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Persistence;
using ActorNet.Serialization;

namespace ActorNet.Demo.Banking;

// Commands.
[ActorMessage(Alias = "bank.deposit")] public sealed record Deposit(decimal Amount, string Reference = "");
[ActorMessage(Alias = "bank.withdraw")] public sealed record Withdraw(decimal Amount, string Reference = "");
[ActorMessage(Alias = "bank.transfer")] public sealed record Transfer(string ToAccount, decimal Amount);
[ActorMessage(Alias = "bank.get-statement")] public sealed record GetStatement(int MaxEntries = 10);

// Events - what actually happened, and what the journal keeps.
[ActorMessage(Alias = "bank.deposited")] public sealed record Deposited(decimal Amount, string Reference, DateTimeOffset At);
[ActorMessage(Alias = "bank.withdrawn")] public sealed record Withdrawn(decimal Amount, string Reference, DateTimeOffset At);
[ActorMessage(Alias = "bank.opened")] public sealed record AccountOpened(string AccountId, DateTimeOffset At);

// Replies.
[ActorMessage(Alias = "bank.statement")]
public sealed record Statement(string AccountId, decimal Balance, int Transactions, IReadOnlyList<string> Recent);

[ActorMessage(Alias = "bank.declined")] public sealed record Declined(string Reason, decimal Balance);
[ActorMessage(Alias = "bank.accepted")] public sealed record Accepted(decimal Balance);

/// <summary>What a fold over the account's events produces.</summary>
public sealed class AccountState
{
    public decimal Balance { get; set; }
    public int Transactions { get; set; }
    public List<string> Recent { get; set; } = [];
}

/// <summary>
/// A bank account as an event-sourced actor.
/// </summary>
/// <remarks>
/// <para>
/// The canonical example for a reason: an account balance is exactly the kind of value that must
/// never be lost to a concurrent update, and the actor model gives that for free - every command
/// for one account is handled by one activation, one at a time, with no lock anywhere in this
/// file.
/// </para>
/// <para>
/// It is event-sourced rather than state-persisted because a bank cannot answer "why is the
/// balance this?" from a single number. The journal is the statement.
/// </para>
/// </remarks>
public sealed class BankAccountActor : EventSourcedActor<AccountState>
{
    /// <summary>Beyond this many events, replaying the whole stream costs more than a snapshot read.</summary>
    protected override long SnapshotEvery => 200;

    protected override void Apply(object domainEvent)
    {
        switch (domainEvent)
        {
            case Deposited d:
                State.Balance += d.Amount;
                Record($"+{d.Amount:N2} {d.Reference}".Trim());
                break;

            case Withdrawn w:
                State.Balance -= w.Amount;
                Record($"-{w.Amount:N2} {w.Reference}".Trim());
                break;

            case AccountOpened opened:
                Record($"opened {opened.At:u}");
                break;
        }
    }

    private void Record(string line)
    {
        State.Transactions++;
        State.Recent.Add(line);

        // The recent list is a display convenience carried in the snapshot, not the ledger. The
        // journal holds everything; this is capped so a hot account's snapshot stays small.
        if (State.Recent.Count > 20) State.Recent.RemoveRange(0, State.Recent.Count - 20);
    }

    protected override async Task ReceiveAsync(object message, CancellationToken cancellationToken)
    {
        // The opening event is written here rather than during recovery, for two reasons: recovery
        // replays history and must not append to it, and deferring it to the mailbox would put it
        // behind the command that woke the actor - so the statement would show the account being
        // opened after its first deposit.
        if (Sequence == 0)
            await PersistAsync(new AccountOpened(Context.Self.Key, DateTimeOffset.UtcNow), cancellationToken);

        switch (message)
        {
            case Deposit deposit when deposit.Amount <= 0:
                await Context.ReplyAsync(new Declined("A deposit must be positive.", State.Balance), cancellationToken);
                break;

            case Deposit deposit:
                await PersistAsync(new Deposited(deposit.Amount, deposit.Reference, DateTimeOffset.UtcNow), cancellationToken);
                await Context.ReplyAsync(new Accepted(State.Balance), cancellationToken);
                break;

            case Withdraw withdraw when withdraw.Amount <= 0:
                await Context.ReplyAsync(new Declined("A withdrawal must be positive.", State.Balance), cancellationToken);
                break;

            case Withdraw withdraw when withdraw.Amount > State.Balance:
                // Refused, so nothing is written: an overdraft that did not happen is not history.
                await Context.ReplyAsync(new Declined("Insufficient funds.", State.Balance), cancellationToken);
                break;

            case Withdraw withdraw:
                await PersistAsync(new Withdrawn(withdraw.Amount, withdraw.Reference, DateTimeOffset.UtcNow), cancellationToken);
                await Context.ReplyAsync(new Accepted(State.Balance), cancellationToken);
                break;

            case Transfer transfer when transfer.Amount > State.Balance:
                await Context.ReplyAsync(new Declined("Insufficient funds for transfer.", State.Balance), cancellationToken);
                break;

            case Transfer transfer:
                // Debit here, then credit the other account by message. This is deliberately not a
                // distributed transaction: the two accounts may live on different nodes, and the
                // honest pattern is a saga - see OrderSagaActor for the version with compensation.
                await PersistAsync(new Withdrawn(transfer.Amount, $"to {transfer.ToAccount}", DateTimeOffset.UtcNow), cancellationToken);
                await Context.TellAsync(
                    ActorId.For<BankAccountActor>(transfer.ToAccount),
                    new Deposit(transfer.Amount, $"from {Context.Self.Key}"),
                    cancellationToken);
                await Context.ReplyAsync(new Accepted(State.Balance), cancellationToken);
                break;

            case GetStatement statement:
                await Context.ReplyAsync(
                    new Statement(
                        Context.Self.Key,
                        State.Balance,
                        State.Transactions,
                        State.Recent.TakeLast(statement.MaxEntries).ToArray()),
                    cancellationToken);
                break;
        }
    }
}
