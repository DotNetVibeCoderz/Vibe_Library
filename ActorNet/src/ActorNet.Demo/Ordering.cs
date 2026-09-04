// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Persistence;
using ActorNet.Serialization;

namespace ActorNet.Demo.Ordering;

[ActorMessage(Alias = "order.place")]
public sealed record PlaceOrder(string CustomerId, string Sku, int Quantity, decimal Total);

[ActorMessage(Alias = "order.get")] public sealed record GetOrder;
[ActorMessage(Alias = "order.reserved")] public sealed record StockReserved(string Sku, int Quantity);
[ActorMessage(Alias = "order.rejected")] public sealed record StockRejected(string Sku, string Reason);
[ActorMessage(Alias = "order.paid")] public sealed record PaymentTaken(decimal Amount);
[ActorMessage(Alias = "order.payment-failed")] public sealed record PaymentFailed(string Reason);

[ActorMessage(Alias = "order.reserve")] public sealed record ReserveStock(string OrderId, string Sku, int Quantity);
[ActorMessage(Alias = "order.release")] public sealed record ReleaseStock(string Sku, int Quantity);
[ActorMessage(Alias = "order.restock")] public sealed record Restock(string Sku, int Quantity);
[ActorMessage(Alias = "order.get-stock")] public sealed record GetStock;
[ActorMessage(Alias = "order.stock")] public sealed record StockLevel(string Sku, int Available, int Reserved);

[ActorMessage(Alias = "order.state")]
public sealed record OrderSnapshot(string OrderId, string Status, string Sku, int Quantity, decimal Total, string? FailureReason);

/// <summary>Where an order is in the saga.</summary>
public enum OrderStatus
{
    New,
    AwaitingStock,
    AwaitingPayment,
    Completed,
    Failed,
}

/// <summary>
/// An order as a saga: a sequence of steps across other actors, with compensation when one fails.
/// </summary>
/// <remarks>
/// <para>
/// This is the honest answer to "how do I do a transaction across actors". There is no distributed
/// transaction here, because the inventory and the payment actor may be on different nodes and
/// two-phase commit across a cluster is exactly what the actor model is trying to avoid. Instead
/// the order remembers where it got to, and undoes the steps it already took when a later one
/// fails - the stock released in <see cref="OrderStatus.AwaitingPayment"/> below.
/// </para>
/// <para>
/// Because it is an actor, the saga's state machine needs no lock and no correlation table: the
/// order id <em>is</em> the address, and every reply for that order arrives at the one activation
/// holding its state.
/// </para>
/// </remarks>
public sealed class OrderSagaActor : PersistentActor<OrderState>
{
    protected override async Task ReceiveAsync(object message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case PlaceOrder order when State.Status != OrderStatus.New:
                Context.Logger.LogDuplicatePlacement(Context.Self.Key, State.Status.ToString());
                break;

            case PlaceOrder order:
                State.CustomerId = order.CustomerId;
                State.Sku = order.Sku;
                State.Quantity = order.Quantity;
                State.Total = order.Total;
                State.Status = OrderStatus.AwaitingStock;
                await SaveStateAsync(cancellationToken);

                await Context.TellAsync(
                    ActorId.For<InventoryActor>(order.Sku),
                    new ReserveStock(Context.Self.Key, order.Sku, order.Quantity),
                    cancellationToken);
                break;

            case StockReserved when State.Status == OrderStatus.AwaitingStock:
                State.Status = OrderStatus.AwaitingPayment;
                await SaveStateAsync(cancellationToken);

                await Context.TellAsync(
                    ActorId.For<PaymentActor>(State.CustomerId),
                    new ChargeCustomer(Context.Self.Key, State.Total),
                    cancellationToken);
                break;

            case StockRejected rejected when State.Status == OrderStatus.AwaitingStock:
                // Nothing to compensate: the first step is the one that failed.
                await FailAsync(rejected.Reason, cancellationToken);
                break;

            case PaymentTaken when State.Status == OrderStatus.AwaitingPayment:
                State.Status = OrderStatus.Completed;
                await SaveStateAsync(cancellationToken);
                break;

            case PaymentFailed failed when State.Status == OrderStatus.AwaitingPayment:
                // Compensation. The stock was reserved by an earlier step that succeeded, and it
                // has to be given back or the inventory leaks on every failed payment.
                await Context.TellAsync(
                    ActorId.For<InventoryActor>(State.Sku),
                    new ReleaseStock(State.Sku, State.Quantity),
                    cancellationToken);
                await FailAsync(failed.Reason, cancellationToken);
                break;

            case GetOrder:
                await Context.ReplyAsync(
                    new OrderSnapshot(Context.Self.Key, State.Status.ToString(), State.Sku, State.Quantity, State.Total, State.FailureReason),
                    cancellationToken);
                break;
        }
    }

    private async Task FailAsync(string reason, CancellationToken cancellationToken)
    {
        State.Status = OrderStatus.Failed;
        State.FailureReason = reason;
        await SaveStateAsync(cancellationToken);
    }
}

/// <summary>An order's position in the saga, persisted so a node move does not lose it.</summary>
public sealed class OrderState
{
    public OrderStatus Status { get; set; } = OrderStatus.New;
    public string CustomerId { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Total { get; set; }
    public string? FailureReason { get; set; }
}

/// <summary>Stock for one SKU.</summary>
public sealed class InventoryState
{
    public int Available { get; set; } = 100;
    public int Reserved { get; set; }
}

/// <summary>
/// One actor per SKU, which is what makes overselling impossible without a database lock.
/// </summary>
/// <remarks>
/// Every reservation for a SKU queues behind the last one on that SKU's single mailbox, so the
/// check and the decrement cannot interleave. Two SKUs are two actors and run in parallel.
/// </remarks>
public sealed class InventoryActor : PersistentActor<InventoryState>
{
    protected override async Task ReceiveAsync(object message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case ReserveStock request when request.Quantity <= State.Available:
                State.Available -= request.Quantity;
                State.Reserved += request.Quantity;
                await SaveStateAsync(cancellationToken);
                await Context.TellAsync(
                    ActorId.For<OrderSagaActor>(request.OrderId),
                    new StockReserved(request.Sku, request.Quantity),
                    cancellationToken);
                break;

            case ReserveStock request:
                await Context.TellAsync(
                    ActorId.For<OrderSagaActor>(request.OrderId),
                    new StockRejected(request.Sku, $"Only {State.Available} of {request.Sku} left."),
                    cancellationToken);
                break;

            case ReleaseStock release:
                State.Reserved = Math.Max(0, State.Reserved - release.Quantity);
                State.Available += release.Quantity;
                await SaveStateAsync(cancellationToken);
                break;

            case Restock restock:
                State.Available += restock.Quantity;
                await SaveStateAsync(cancellationToken);
                break;

            case GetStock:
                await Context.ReplyAsync(new StockLevel(Context.Self.Key, State.Available, State.Reserved), cancellationToken);
                break;
        }
    }
}

[ActorMessage(Alias = "order.charge")] public sealed record ChargeCustomer(string OrderId, decimal Amount);
[ActorMessage(Alias = "order.set-limit")] public sealed record SetCreditLimit(decimal Limit);

/// <summary>A customer's credit.</summary>
public sealed class PaymentState
{
    public decimal CreditLimit { get; set; } = 500m;
    public decimal Charged { get; set; }
}

/// <summary>Charges a customer, refusing anything over their remaining credit.</summary>
public sealed class PaymentActor : PersistentActor<PaymentState>
{
    protected override async Task ReceiveAsync(object message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case ChargeCustomer charge when State.Charged + charge.Amount > State.CreditLimit:
                await Context.TellAsync(
                    ActorId.For<OrderSagaActor>(charge.OrderId),
                    new PaymentFailed($"Charge of {charge.Amount:N2} exceeds the remaining credit of {State.CreditLimit - State.Charged:N2}."),
                    cancellationToken);
                break;

            case ChargeCustomer charge:
                State.Charged += charge.Amount;
                await SaveStateAsync(cancellationToken);
                await Context.TellAsync(ActorId.For<OrderSagaActor>(charge.OrderId), new PaymentTaken(charge.Amount), cancellationToken);
                break;

            case SetCreditLimit limit:
                State.CreditLimit = limit.Limit;
                await SaveStateAsync(cancellationToken);
                break;
        }
    }
}

internal static partial class OrderingLog
{
    [Microsoft.Extensions.Logging.LoggerMessage(
        EventId = 3001,
        Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "Order {OrderId} was placed again while already {Status}; ignoring the duplicate.")]
    public static partial void LogDuplicatePlacement(this Microsoft.Extensions.Logging.ILogger logger, string orderId, string status);
}
