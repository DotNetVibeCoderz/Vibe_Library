// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Demo.Banking;
using ActorNet.Demo.Telemetry;
using ActorNet.Streams;

namespace ActorNet.Cli;

/// <summary>
/// Synthetic traffic, so the monitor and the dashboard have something real to display.
/// </summary>
/// <remarks>
/// Shaped like a plausible workload rather than a flat blast: a wide fleet of devices reporting
/// steadily, a narrower set of accounts taking transactions, and an occasional device running hot
/// enough to trip an alarm. A uniform stream would make every panel look the same and would hide
/// exactly the behaviour worth watching.
/// </remarks>
internal static class LoadGenerator
{
    /// <summary>Runs until cancelled.</summary>
    public static async Task Run(IActorSystem system, CancellationToken cancellationToken, int devices = 40, int accounts = 12)
    {
        var telemetry = TelemetryStream(system, devices, cancellationToken);
        var banking = BankingStream(system, accounts, cancellationToken);

        try
        {
            await Task.WhenAll(telemetry, banking);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C. Nothing to report.
        }
    }

    private static Task TelemetryStream(IActorSystem system, int devices, CancellationToken cancellationToken)
    {
        var random = new Random(20260904);

        return ActorStream
            .Interval(TimeSpan.FromMilliseconds(25), tick => tick)
            .Select(tick =>
            {
                var device = (int)(tick % devices);

                // One device in the fleet is deliberately over temperature, so the alarm path and
                // its hysteresis are visible instead of theoretical.
                var baseline = device == 3 ? 78.0 : 45.0;
                return new SensorReading($"sensor-{device:D3}", baseline + random.NextDouble() * 12, DateTimeOffset.UtcNow);
            })
            .ToActorsAsync(system, reading => ActorId.For<DeviceActor>(reading.DeviceId), cancellationToken);
    }

    private static async Task BankingStream(IActorSystem system, int accounts, CancellationToken cancellationToken)
    {
        var random = new Random(4092026);

        while (!cancellationToken.IsCancellationRequested)
        {
            var account = ActorId.For<BankAccountActor>($"acct-{random.Next(accounts):D3}");
            var amount = Math.Round((decimal)(random.NextDouble() * 200), 2);

            object command = random.Next(3) == 0
                ? new Withdraw(amount, "atm")
                : new Deposit(amount, "salary");

            await system.TellAsync(account, command, default, cancellationToken);
            await Task.Delay(40, cancellationToken);
        }
    }
}
