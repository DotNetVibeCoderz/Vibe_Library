// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.ObjectModel;
using ActorNet.Demo.Ordering;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ActorNet.Samples.Avalonia.ViewModels;

/// <summary>One order row.</summary>
public sealed partial class OrderRow(string id) : ObservableObject
{
    public string Id { get; } = id;

    [ObservableProperty] private string _status = "New";
    [ObservableProperty] private int _quantity;
    [ObservableProperty] private decimal _total;
    [ObservableProperty] private string _why = "-";
}

/// <summary>
/// An order saga across the inventory and payment actors.
/// </summary>
/// <remarks>
/// There is no distributed transaction here and the sample is built to show why that is fine: the
/// "oversell" button fires many orders at one SKU at once, and the inventory actor's single
/// mailbox is the only thing stopping them from interleaving a check with a decrement. The stock
/// number afterwards is the assertion.
/// </remarks>
public sealed partial class OrderingViewModel : ScenarioViewModel
{
    private const string Sku = "widget";
    private const string Customer = "cust-desktop";
    private int _nextOrder = 1;

    public ObservableCollection<OrderRow> Orders { get; } = [];

    [ObservableProperty] private int _available;
    [ObservableProperty] private int _reserved;
    [ObservableProperty] private int _quantity = 2;
    [ObservableProperty] private decimal _total = 120m;
    [ObservableProperty] private decimal _creditLimit = 400m;

    public OrderingViewModel(ActorSystem system)
        : base(system, "Ordering",
            "No two-phase commit: the saga remembers where it got to and releases what it already reserved when a later step fails.")
    {
    }

    [RelayCommand]
    private Task PlaceAsync() => RunAsync(async () =>
    {
        var id = $"order-{_nextOrder++:D3}";
        Orders.Insert(0, new OrderRow(id));

        await System.TellAsync(ActorId.For<OrderSagaActor>(id), new PlaceOrder(Customer, Sku, Quantity, Total));
        Say($"Placed {id}: {Quantity} x {Sku} for {Total:N2}.");

        // The saga is several hops: reserve stock, then charge, then settle or compensate.
        await Task.Delay(400);
    });

    [RelayCommand]
    private Task RestockAsync() => RunAsync(async () =>
    {
        await System.TellAsync(ActorId.For<InventoryActor>(Sku), new Restock(Sku, 10));
        Say($"Restocked {Sku} by 10.");
        await Task.Delay(150);
    });

    [RelayCommand]
    private Task SetCreditAsync() => RunAsync(async () =>
    {
        await System.TellAsync(ActorId.For<PaymentActor>(Customer), new SetCreditLimit(CreditLimit));
        Say($"Credit limit for {Customer} set to {CreditLimit:N2}. A charge past it fails the saga at the payment step.");
        await Task.Delay(150);
    });

    [RelayCommand]
    private Task OversellAsync() => RunAsync(async () =>
    {
        var stock = await StockAsync();
        var attempts = stock.Available + 6;

        Say($"{stock.Available} in stock. Firing {attempts} single-unit orders at once - at most {stock.Available} may succeed.");

        var ids = Enumerable.Range(0, attempts).Select(_ => $"order-{_nextOrder++:D3}").ToArray();
        foreach (var id in ids) Orders.Insert(0, new OrderRow(id));

        await Task.WhenAll(ids.Select(id =>
            System.TellAsync(ActorId.For<OrderSagaActor>(id), new PlaceOrder(Customer, Sku, 1, 1m)).AsTask()));

        await Task.Delay(1200);

        var after = await StockAsync();
        Say(after.Available >= 0
            ? $"Stock is {after.Available} available, {after.Reserved} reserved. It never went negative, and InventoryActor holds no lock."
            : $"Stock went negative at {after.Available}. That is an oversell and a bug worth reporting.");
    });

    [RelayCommand]
    private Task ResetAsync() => RunAsync(async () =>
    {
        foreach (var order in Orders) await System.DeactivateAsync(ActorId.For<OrderSagaActor>(order.Id));
        Orders.Clear();
        Say("Cleared the order list. The sagas themselves are still in the store, keyed by their ids.");
    });

    protected override async Task RefreshAsync()
    {
        var stock = await StockAsync();
        Available = stock.Available;
        Reserved = stock.Reserved;

        // Only the visible rows: an oversell run creates dozens, and asking every one of them
        // every 700 ms would swamp the node with the sample's own polling.
        foreach (var row in Orders.Take(25))
        {
            var snapshot = await System.AskAsync<OrderSnapshot>(
                ActorId.For<OrderSagaActor>(row.Id), new GetOrder(), TimeSpan.FromSeconds(10));

            row.Status = snapshot.Status;
            row.Quantity = snapshot.Quantity;
            row.Total = snapshot.Total;
            row.Why = snapshot.FailureReason ?? "-";
        }
    }

    private Task<StockLevel> StockAsync() =>
        System.AskAsync<StockLevel>(ActorId.For<InventoryActor>(Sku), new GetStock(), TimeSpan.FromSeconds(10));
}
