// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Persistence;
using ActorNet.Serialization;

namespace ActorNet.Demo.Telemetry;

[ActorMessage(Alias = "iot.reading")]
public sealed record SensorReading(string DeviceId, double Celsius, DateTimeOffset At);

[ActorMessage(Alias = "iot.get-status")] public sealed record GetDeviceStatus;
[ActorMessage(Alias = "iot.calibrate")] public sealed record Calibrate(double Offset);

[ActorMessage(Alias = "iot.status")]
public sealed record DeviceStatus(
    string DeviceId,
    double Latest,
    double Average,
    double Minimum,
    double Maximum,
    long Readings,
    int AlarmsRaised,
    bool InAlarm);

[ActorMessage(Alias = "iot.alarm")]
public sealed record TemperatureAlarm(string DeviceId, double Celsius, DateTimeOffset At);

[ActorMessage(Alias = "iot.alarm-cleared")]
public sealed record AlarmCleared(string DeviceId, DateTimeOffset At);

/// <summary>Rolling statistics for one device.</summary>
public sealed class DeviceState
{
    public double Latest { get; set; }
    public double Sum { get; set; }
    public double Minimum { get; set; } = double.MaxValue;
    public double Maximum { get; set; } = double.MinValue;
    public long Readings { get; set; }
    public double Calibration { get; set; }
    public int AlarmsRaised { get; set; }
    public bool InAlarm { get; set; }
}

/// <summary>
/// One actor per physical device, fed by a stream of readings.
/// </summary>
/// <remarks>
/// <para>
/// The shape that makes IoT ingest tractable: a million devices are a million addresses, but only
/// the ones currently reporting are activated, and the idle sweeper retires the rest. Memory
/// tracks the active fleet rather than the registered one.
/// </para>
/// <para>
/// State-persisted rather than event-sourced, unlike the bank account. Individual readings are
/// not worth keeping forever - the rolling aggregate is the thing that matters, and a device that
/// reports once a second would produce an unbounded journal for no benefit.
/// </para>
/// </remarks>
public sealed class DeviceActor : PersistentActor<DeviceState>
{
    /// <summary>Alarm above this, in degrees Celsius.</summary>
    public const double AlarmAbove = 80.0;

    /// <summary>
    /// Clear the alarm below this. The gap from <see cref="AlarmAbove"/> is deliberate: without
    /// hysteresis, a device sitting exactly at the threshold raises and clears an alarm on every
    /// reading.
    /// </summary>
    public const double ClearBelow = 75.0;

    /// <summary>Readings arrive continuously, so checkpoint periodically instead of only on the way out.</summary>
    protected override int SaveEvery => 100;

    protected override async Task ReceiveAsync(object message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case SensorReading reading:
                await HandleReadingAsync(reading, cancellationToken);
                break;

            case Calibrate calibrate:
                State.Calibration = calibrate.Offset;
                await SaveStateAsync(cancellationToken);
                break;

            case GetDeviceStatus:
                await Context.ReplyAsync(
                    new DeviceStatus(
                        Context.Self.Key,
                        State.Latest,
                        State.Readings == 0 ? 0 : State.Sum / State.Readings,
                        State.Readings == 0 ? 0 : State.Minimum,
                        State.Readings == 0 ? 0 : State.Maximum,
                        State.Readings,
                        State.AlarmsRaised,
                        State.InAlarm),
                    cancellationToken);
                break;
        }
    }

    private async Task HandleReadingAsync(SensorReading reading, CancellationToken cancellationToken)
    {
        var value = reading.Celsius + State.Calibration;

        State.Latest = value;
        State.Sum += value;
        State.Readings++;
        State.Minimum = Math.Min(State.Minimum, value);
        State.Maximum = Math.Max(State.Maximum, value);

        switch (State.InAlarm)
        {
            case false when value > AlarmAbove:
                State.InAlarm = true;
                State.AlarmsRaised++;
                await Context.TellAsync(ActorId.For<AlarmDeskActor>("main"), new TemperatureAlarm(Context.Self.Key, value, reading.At), cancellationToken);
                break;

            case true when value < ClearBelow:
                State.InAlarm = false;
                await Context.TellAsync(ActorId.For<AlarmDeskActor>("main"), new AlarmCleared(Context.Self.Key, reading.At), cancellationToken);
                break;
        }
    }
}

[ActorMessage(Alias = "iot.get-alarms")] public sealed record GetActiveAlarms;

[ActorMessage(Alias = "iot.alarms")]
public sealed record ActiveAlarms(IReadOnlyList<string> Devices, int RaisedTotal);

/// <summary>
/// A single actor that every device reports alarms to.
/// </summary>
/// <remarks>
/// A deliberate fan-in point. It is a bottleneck by design - one mailbox, one thread of control -
/// which is what makes "the set of devices currently in alarm" a value that is always consistent
/// rather than a query across a million actors.
/// </remarks>
public sealed class AlarmDeskActor : ReceiveActor
{
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);
    private int _raisedTotal;

    public AlarmDeskActor()
    {
        On<TemperatureAlarm>(alarm =>
        {
            if (_active.Add(alarm.DeviceId)) _raisedTotal++;
            Context.Logger.LogAlarmRaised(alarm.DeviceId, alarm.Celsius);
        });

        On<AlarmCleared>(cleared => _active.Remove(cleared.DeviceId));

        On<GetActiveAlarms>(async (_, ct) =>
            await Context.ReplyAsync(new ActiveAlarms(_active.Order(StringComparer.Ordinal).ToArray(), _raisedTotal), ct));
    }
}

internal static partial class TelemetryLog
{
    [Microsoft.Extensions.Logging.LoggerMessage(
        EventId = 2001,
        Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "Device {DeviceId} is over temperature at {Celsius:F1} C.")]
    public static partial void LogAlarmRaised(this Microsoft.Extensions.Logging.ILogger logger, string deviceId, double celsius);
}
