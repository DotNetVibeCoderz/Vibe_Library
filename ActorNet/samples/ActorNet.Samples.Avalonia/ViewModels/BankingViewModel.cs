// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.ObjectModel;
using ActorNet.Demo.Banking;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ActorNet.Samples.Avalonia.ViewModels;

/// <summary>One account row in the ledger table.</summary>
public sealed partial class AccountRow(string id) : ObservableObject
{
    public string Id { get; } = id;

    [ObservableProperty] private decimal _balance;
    [ObservableProperty] private int _transactions;
    [ObservableProperty] private string _recent = string.Empty;
}

/// <summary>
/// Event-sourced bank accounts.
/// </summary>
/// <remarks>
/// The concurrency button is the point of the whole screen: it fires several hundred deposits at
/// one account from every core at once, with no lock anywhere in the actor. If the total is ever
/// short, the single-writer guarantee is broken - so the sample states the expected figure before
/// running rather than after, which is the only way an assertion like this means anything.
/// </remarks>
public sealed partial class BankingViewModel : ScenarioViewModel
{
    private static readonly string[] AccountIds = ["alice", "bob", "citra", "dewi"];

    public ObservableCollection<AccountRow> Accounts { get; } = [];

    [ObservableProperty] private string _selectedAccount = "alice";
    [ObservableProperty] private decimal _amount = 100m;
    [ObservableProperty] private string _transferTo = "bob";

    public BankingViewModel(ActorSystem system)
        : base(system, "Banking",
            "A balance cannot be lost to a concurrent update: every command for one account is handled by one activation, one at a time.")
    {
        foreach (var id in AccountIds) Accounts.Add(new AccountRow(id));
    }

    public IReadOnlyList<string> AccountChoices => AccountIds;

    [RelayCommand]
    private Task DepositAsync() => RunAsync(async () =>
    {
        var reply = await System.AskAsync<object>(Account(SelectedAccount), new Deposit(Amount, "manual"), TimeSpan.FromSeconds(10));
        Say(reply switch
        {
            Accepted accepted => $"Deposited {Amount:N2} into {SelectedAccount}. Balance {accepted.Balance:N2}.",
            Declined declined => $"Refused: {declined.Reason}",
            _ => $"Unexpected reply {reply.GetType().Name}.",
        });
    });

    [RelayCommand]
    private Task WithdrawAsync() => RunAsync(async () =>
    {
        var reply = await System.AskAsync<object>(Account(SelectedAccount), new Withdraw(Amount, "manual"), TimeSpan.FromSeconds(10));
        Say(reply switch
        {
            Accepted accepted => $"Withdrew {Amount:N2} from {SelectedAccount}. Balance {accepted.Balance:N2}.",
            Declined declined => $"Refused: {declined.Reason} Balance stays at {declined.Balance:N2}.",
            _ => $"Unexpected reply {reply.GetType().Name}.",
        });
    });

    [RelayCommand]
    private Task TransferAsync() => RunAsync(async () =>
    {
        if (TransferTo == SelectedAccount)
        {
            Say("A transfer needs two different accounts.");
            return;
        }

        var reply = await System.AskAsync<object>(Account(SelectedAccount), new Transfer(TransferTo, Amount), TimeSpan.FromSeconds(10));
        Say(reply switch
        {
            Accepted => $"Debited {Amount:N2} from {SelectedAccount}; {TransferTo} is credited by message.",
            Declined declined => $"Refused: {declined.Reason}",
            _ => $"Unexpected reply {reply.GetType().Name}.",
        });
    });

    [RelayCommand]
    private Task HammerAsync() => RunAsync(async () =>
    {
        const int senders = 16;
        const int each = 50;
        const decimal unit = 1m;

        var before = await StatementAsync(SelectedAccount);
        var expected = before.Balance + senders * each * unit;
        Say($"Sending {senders * each} deposits of {unit:N2} from {senders} tasks. Expecting exactly {expected:N2}.");

        await Task.WhenAll(Enumerable.Range(0, senders).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < each; i++) await System.TellAsync(Account(SelectedAccount), new Deposit(unit, "concurrent"));
        })));

        var after = await StatementAsync(SelectedAccount);
        Say(after.Balance == expected
            ? $"Balance is {after.Balance:N2}. Not one update lost, and the actor has no lock in it."
            : $"Balance is {after.Balance:N2} but should be {expected:N2}. That is a bug worth reporting.");
    });

    [RelayCommand]
    private Task DeactivateAsync() => RunAsync(async () =>
    {
        var before = await StatementAsync(SelectedAccount);
        await System.DeactivateAsync(Account(SelectedAccount));
        Say($"Deactivated {SelectedAccount}. It is gone from memory; the journal still holds {before.Transactions} events.");

        var after = await StatementAsync(SelectedAccount);
        Say($"Asked again: reactivated and replayed to {after.Balance:N2}. The caller never knew it went away.");
    });

    protected override async Task RefreshAsync()
    {
        foreach (var row in Accounts)
        {
            var statement = await StatementAsync(row.Id);
            row.Balance = statement.Balance;
            row.Transactions = statement.Transactions;
            row.Recent = statement.Recent.Count > 0 ? statement.Recent[^1] : "-";
        }
    }

    private Task<Statement> StatementAsync(string id) =>
        System.AskAsync<Statement>(Account(id), new GetStatement(5), TimeSpan.FromSeconds(15));

    private static ActorId Account(string id) => ActorId.For<BankAccountActor>(id);
}
