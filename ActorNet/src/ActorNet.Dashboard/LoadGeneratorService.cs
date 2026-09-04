// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Demo.Banking;
using ActorNet.Demo.Telemetry;
using ActorNet.Streams;

namespace ActorNet.Dashboard;

/// <summary>
/// Optional synthetic traffic, so the console has something to display on a machine with no real
/// workload attached.
/// </summary>
/// <remarks>
/// Shaped like a plausible workload rather than a flat blast: a wide device fleet reporting
/// steadily, a narrower set of accounts taking transactions, and one device deliberately running
/// hot enough to trip an alarm. Uniform traffic would make every panel look identical and would
/// hide exactly the behaviour worth watching.
/// </remarks>
internal sealed class LoadGeneratorService(ActorSystem system, ILogger<LoadGeneratorService> logger) : BackgroundService
{
    private const int Devices = 60;
    private const int Accounts = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Generating synthetic traffic across {Devices} devices and {Accounts} accounts.", Devices, Accounts);

        try
        {
            await Task.WhenAll(
                TelemetryAsync(stoppingToken),
                BankingAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private Task TelemetryAsync(CancellationToken cancellationToken)
    {
        var random = new Random(20260904);

        return ActorStream
            .Interval(TimeSpan.FromMilliseconds(20), tick => tick)
            .Select(tick =>
            {
                var device = (int)(tick % Devices);
                var baseline = device == 11 ? 78.0 : 44.0;
                return new SensorReading($"sensor-{device:D3}", baseline + random.NextDouble() * 12, DateTimeOffset.UtcNow);
            })
            .ToActorsAsync(system, reading => ActorId.For<DeviceActor>(reading.DeviceId), cancellationToken);
    }

    private async Task BankingAsync(CancellationToken cancellationToken)
    {
        var random = new Random(4092026);

        while (!cancellationToken.IsCancellationRequested)
        {
            var account = ActorId.For<BankAccountActor>($"acct-{random.Next(Accounts):D3}");
            var amount = Math.Round((decimal)(random.NextDouble() * 200), 2);

            object command = random.Next(4) == 0
                ? new Withdraw(amount, "atm")
                : new Deposit(amount, "salary");

            await system.TellAsync(account, command, default, cancellationToken);
            await Task.Delay(60, cancellationToken);
        }
    }
}
