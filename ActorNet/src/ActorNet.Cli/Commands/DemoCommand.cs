// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Demo;
using ActorNet.Demo.Banking;
using ActorNet.Demo.Ordering;
using ActorNet.Demo.Telemetry;
using ActorNet.Streams;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ActorNet.Cli.Commands;

public sealed class DemoSettings : NodeSettings
{
    [CommandArgument(0, "[scenario]")]
    [System.ComponentModel.Description("banking, telemetry, ordering, or lifecycle. Omit for an interactive menu.")]
    public string? Scenario { get; init; }
}

/// <summary>Runs the demo scenarios, either from a menu or straight from the command line.</summary>
public sealed class DemoCommand : AsyncCommand<DemoSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, DemoSettings settings, CancellationToken cancellationToken)
    {
        Theme.Banner();

        await using var system = await NodeFactory.StartAsync(settings, networking: settings.Seeds.Length > 0);
        NodeFactory.Describe(system);

        var scenario = settings.Scenario?.ToLowerInvariant() ?? Prompt();
        if (scenario is "quit") return 0;

        return scenario switch
        {
            "banking" => await BankingAsync(system),
            "telemetry" => await TelemetryAsync(system),
            "ordering" => await OrderingAsync(system),
            "lifecycle" => await LifecycleAsync(system),
            _ => Unknown(scenario),
        };
    }

    private static string Prompt()
    {
        var choices = DemoCatalog.Scenarios
            .Select(s => $"{s.Name.ToLowerInvariant()} - {s.Summary}")
            .Append("lifecycle - Watch an actor activate, persist, deactivate on idle, and come back with its state.")
            .Append("quit")
            .ToArray();

        var chosen = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Pick a [{Theme.Accent}]scenario[/]:")
                .HighlightStyle(Style.Parse(Theme.Accent))
                .PageSize(10)
                .AddChoices(choices));

        return chosen.Split(' ')[0];
    }

    private static int Unknown(string scenario)
    {
        Theme.Fail($"Unknown scenario '{scenario.Safe()}'. Try banking, telemetry, ordering, or lifecycle.");
        return 1;
    }

    private static async Task<int> BankingAsync(ActorSystem system)
    {
        Theme.Rule("Banking - event-sourced accounts");
        AnsiConsole.MarkupLine($"[{Theme.Muted}]Balances are a fold over a journal, not a stored number. Nothing here takes a lock.[/]\n");

        var alice = ActorId.For<BankAccountActor>("alice");
        var bob = ActorId.For<BankAccountActor>("bob");

        await system.AskAsync<Accepted>(alice, new Deposit(1_000m, "opening"));
        await system.AskAsync<Accepted>(bob, new Deposit(250m, "opening"));
        Theme.Success("Opened two accounts.");

        // 200 concurrent deposits into one account. If the balance below is not exactly 3,000
        // more than it started, the single-writer guarantee is broken.
        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => system.TellAsync(alice, new Deposit(15m, $"batch-{i}")).AsTask()));
        Theme.Success("Sent 200 concurrent deposits to one account.");

        var declined = await system.AskAsync<Declined>(bob, new Withdraw(10_000m, "atm"));
        Theme.Caution($"Overdraft refused: {declined.Reason.Safe()} Balance stays at {declined.Balance:N2}.");

        await system.AskAsync<Accepted>(alice, new Transfer("bob", 500m));
        Theme.Success("Transferred 500.00 from alice to bob.");

        // The transfer credits bob by message, so give the second hop a moment to land.
        await Task.Delay(200);

        foreach (var account in new[] { alice, bob })
        {
            var statement = await system.AskAsync<Statement>(account, new GetStatement(6));
            var table = Theme.Grid("Account", "Balance", "Transactions");
            table.AddRow(statement.AccountId.Safe(), $"[{Theme.Accent}]{statement.Balance:N2}[/]", $"{statement.Transactions:N0}");
            AnsiConsole.Write(table);

            foreach (var line in statement.Recent) AnsiConsole.MarkupLine($"   [{Theme.Muted}]{line.Safe()}[/]");
            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine($"[{Theme.Muted}]alice: 1,000 opening + 200 x 15 = 4,000, less the 500 transferred.[/]");
        return 0;
    }

    private static async Task<int> TelemetryAsync(ActorSystem system)
    {
        Theme.Rule("Telemetry - a reactive stream into one actor per device");
        AnsiConsole.MarkupLine($"[{Theme.Muted}]Readings are routed by device id, so each device's history lands on its own activation.[/]\n");

        const int devices = 25;
        var random = new Random(20260904);

        var sent = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse(Theme.Accent))
            .StartAsync("Streaming 5,000 readings...", async _ =>
                await ActorStream.From(Enumerable.Range(0, 5_000))
                    .Select(i =>
                    {
                        var device = i % devices;

                        // Two devices run hot, so the alarm path and its hysteresis are exercised.
                        var baseline = device is 7 or 19 ? 76.0 : 40.0;
                        return new SensorReading($"sensor-{device:D3}", baseline + random.NextDouble() * 14, DateTimeOffset.UtcNow);
                    })
                    .Batch(200)
                    .SelectAsync(async (batch, ct) =>
                    {
                        foreach (var reading in batch)
                            await system.TellAsync(ActorId.For<DeviceActor>(reading.DeviceId), reading, default, ct);
                        return batch.Count;
                    })
                    .RunAsync());

        Theme.Success($"Streamed {sent * 200:N0} readings across {devices} devices.");
        await Task.Delay(300);

        var table = Theme.Grid("Device", "Latest", "Average", "Min", "Max", "Readings", "Alarms");
        foreach (var device in Enumerable.Range(0, devices).Take(8))
        {
            var status = await system.AskAsync<DeviceStatus>(ActorId.For<DeviceActor>($"sensor-{device:D3}"), new GetDeviceStatus());
            table.AddRow(
                status.DeviceId.Safe(),
                status.InAlarm ? $"[{Theme.Bad}]{status.Latest:N1}[/]" : $"{status.Latest:N1}",
                $"{status.Average:N1}",
                $"[{Theme.Muted}]{status.Minimum:N1}[/]",
                $"[{Theme.Muted}]{status.Maximum:N1}[/]",
                $"{status.Readings:N0}",
                status.AlarmsRaised > 0 ? $"[{Theme.Warn}]{status.AlarmsRaised}[/]" : $"[{Theme.Muted}]0[/]");
        }

        AnsiConsole.Write(table);

        var alarms = await system.AskAsync<ActiveAlarms>(ActorId.For<AlarmDeskActor>("main"), new GetActiveAlarms());
        AnsiConsole.WriteLine();
        if (alarms.Devices.Count > 0)
            Theme.Caution($"The alarm desk holds {alarms.Devices.Count} active alarm(s): {string.Join(", ", alarms.Devices).Safe()}");
        else
            Theme.Info($"No active alarms; {alarms.RaisedTotal} were raised and cleared during the run.");

        AnsiConsole.MarkupLine(
            $"[{Theme.Muted}]The desk is a single actor on purpose: one mailbox means the alarm set is always consistent.[/]");
        return 0;
    }

    private static async Task<int> OrderingAsync(ActorSystem system)
    {
        Theme.Rule("Ordering - a saga with compensation");
        AnsiConsole.MarkupLine($"[{Theme.Muted}]No distributed transaction. The saga remembers its position and undoes what it already did.[/]\n");

        await system.TellAsync(ActorId.For<InventoryActor>("widget"), new Restock("widget", 5));
        await system.TellAsync(ActorId.For<PaymentActor>("cust-1"), new SetCreditLimit(400m));
        await Task.Delay(100);

        // Succeeds: stock is available and the total is inside the credit limit.
        await system.TellAsync(ActorId.For<OrderSagaActor>("order-1"), new PlaceOrder("cust-1", "widget", 2, 300m));

        // Fails at the payment step, after stock was already reserved - which is what makes the
        // compensating release observable.
        await system.TellAsync(ActorId.For<OrderSagaActor>("order-2"), new PlaceOrder("cust-1", "widget", 2, 900m));

        // Fails at the first step: not enough stock left, so there is nothing to compensate.
        await system.TellAsync(ActorId.For<OrderSagaActor>("order-3"), new PlaceOrder("cust-1", "widget", 999, 50m));

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse(Theme.Accent))
            .StartAsync("Running three orders through the saga...", async _ => await Task.Delay(800));

        var table = Theme.Grid("Order", "Status", "Item", "Qty", "Total", "Why");
        foreach (var order in new[] { "order-1", "order-2", "order-3" })
        {
            var snapshot = await system.AskAsync<OrderSnapshot>(ActorId.For<OrderSagaActor>(order), new GetOrder());
            var status = snapshot.Status switch
            {
                "Completed" => $"[{Theme.Good}]{snapshot.Status}[/]",
                "Failed" => $"[{Theme.Bad}]{snapshot.Status}[/]",
                _ => $"[{Theme.Warn}]{snapshot.Status}[/]",
            };

            table.AddRow(
                snapshot.OrderId.Safe(),
                status,
                snapshot.Sku.Safe(),
                $"{snapshot.Quantity}",
                $"{snapshot.Total:N2}",
                $"[{Theme.Muted}]{(snapshot.FailureReason ?? "-").Safe()}[/]");
        }

        AnsiConsole.Write(table);

        var stock = await system.AskAsync<StockLevel>(ActorId.For<InventoryActor>("widget"), new GetStock());
        AnsiConsole.WriteLine();
        Theme.Info($"Stock now: {stock.Available} available, {stock.Reserved} reserved.");
        AnsiConsole.MarkupLine(
            $"[{Theme.Muted}]Started at 105. order-1 kept 2. order-2's 2 were reserved and then released when payment failed.[/]");
        return 0;
    }

    private static async Task<int> LifecycleAsync(ActorSystem system)
    {
        Theme.Rule("Lifecycle - what makes an actor 'virtual'");

        var id = ActorId.For<BankAccountActor>("lifecycle-demo");

        AnsiConsole.MarkupLine($"[{Theme.Muted}]1. Nothing is created. The address exists whether or not anything is running.[/]");
        Theme.Info($"Active actors on this node: {system.LocalActors.Count}");

        AnsiConsole.MarkupLine($"\n[{Theme.Muted}]2. The first message activates it.[/]");
        await system.AskAsync<Accepted>(id, new Deposit(400m, "first"));
        Theme.Success($"{id} is activated. Active actors: {system.LocalActors.Count}");

        AnsiConsole.MarkupLine($"\n[{Theme.Muted}]3. Deactivate it. State is flushed on the way out.[/]");
        await system.DeactivateAsync(id);
        Theme.Success($"Deactivated. Active actors: {system.LocalActors.Count}");

        AnsiConsole.MarkupLine($"\n[{Theme.Muted}]4. The next message reactivates it - same address, state recovered from the journal.[/]");
        var statement = await system.AskAsync<Statement>(id, new GetStatement());
        Theme.Success($"Balance is {statement.Balance:N2} after {statement.Transactions} recorded events.");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[{Theme.Muted}]The caller never learned that the actor went away. That is the whole idea: no lifecycle management,[/]\n" +
            $"[{Theme.Muted}]and memory tracks the actors currently working rather than every actor that has ever existed.[/]");

        var snapshot = system.Metrics.Snapshot(includeActors: false);
        AnsiConsole.WriteLine();
        AnsiConsole.Write(Theme.Facts("Counters")
            .Fact("Activations", $"{snapshot.Activations}")
            .Fact("Deactivations", $"{snapshot.Deactivations}")
            .Fact("Messages", $"{snapshot.MessagesProcessed}"));

        return 0;
    }
}
