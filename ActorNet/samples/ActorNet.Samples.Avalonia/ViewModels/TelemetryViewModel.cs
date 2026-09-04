// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.ObjectModel;
using ActorNet.Demo.Telemetry;
using ActorNet.Streams;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ActorNet.Samples.Avalonia.ViewModels;

/// <summary>One device row.</summary>
public sealed partial class DeviceRow(string id) : ObservableObject
{
    public string Id { get; } = id;

    [ObservableProperty] private double _latest;
    [ObservableProperty] private double _average;
    [ObservableProperty] private double _minimum;
    [ObservableProperty] private double _maximum;
    [ObservableProperty] private long _readings;
    [ObservableProperty] private bool _inAlarm;
    [ObservableProperty] private int _alarms;
}

/// <summary>
/// A reactive stream of sensor readings routed into one actor per device.
/// </summary>
/// <remarks>
/// The stream runs while the screen is visible and stops when it is not - which is also what makes
/// the idle sweeper observable here: leave the screen, come back, and the device actors have been
/// deactivated and will reactivate from their persisted aggregates.
/// </remarks>
public sealed partial class TelemetryViewModel : ScenarioViewModel
{
    private const int Devices = 12;
    private CancellationTokenSource? _streaming;

    public ObservableCollection<DeviceRow> Devices_ { get; } = [];

    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private string _activeAlarms = "none";
    [ObservableProperty] private double _hotDeviceOffset;

    public TelemetryViewModel(ActorSystem system)
        : base(system, "Telemetry",
            "A million devices are a million addresses, but only the ones reporting are activated - so memory tracks the active fleet, not the registered one.")
    {
        for (var i = 0; i < Devices; i++) Devices_.Add(new DeviceRow($"sensor-{i:D3}"));
    }

    [RelayCommand]
    private void ToggleStream()
    {
        if (IsStreaming)
        {
            StopStream();
            Say("Stream stopped. The device actors stay activated until the sweeper retires them.");
            return;
        }

        _streaming = new CancellationTokenSource();
        IsStreaming = true;
        Say($"Streaming readings into {Devices} device actors, routed by device id.");

        _ = ActorStream
            .Interval(TimeSpan.FromMilliseconds(30), tick => tick)
            .Select(tick =>
            {
                var device = (int)(tick % Devices);

                // Device 4 is the one that runs hot, so the alarm path and its hysteresis are
                // exercised rather than merely described.
                var baseline = device == 4 ? 74.0 + HotDeviceOffset : 42.0;
                return new SensorReading($"sensor-{device:D3}", baseline + Random.Shared.NextDouble() * 10, DateTimeOffset.UtcNow);
            })
            .ToActorsAsync(System, reading => ActorId.For<DeviceActor>(reading.DeviceId), _streaming.Token)
            .ContinueWith(_ => { }, TaskScheduler.Default);
    }

    [RelayCommand]
    private Task HeatUpAsync() => RunAsync(async () =>
    {
        HotDeviceOffset += 6;
        Say($"Raised sensor-004's baseline by 6 C. It trips at {DeviceActor.AlarmAbove:N0} C and clears below {DeviceActor.ClearBelow:N0} C.");
        await Task.CompletedTask;
    });

    [RelayCommand]
    private Task CoolDownAsync() => RunAsync(async () =>
    {
        HotDeviceOffset = -20;
        Say("Cooled sensor-004 well below the clear threshold. Its alarm should drop on the next reading.");
        await Task.CompletedTask;
    });

    [RelayCommand]
    private Task SweepAsync() => RunAsync(async () =>
    {
        var before = System.LocalActors.Count;
        foreach (var device in Devices_) await System.DeactivateAsync(ActorId.For<DeviceActor>(device.Id));

        Say($"Deactivated every device actor ({before} -> {System.LocalActors.Count} local actors). Aggregates were flushed on the way out.");
        Say("The next reading reactivates each one with its counts intact - that is the virtual actor lifecycle.");
    });

    public override void Suspend()
    {
        base.Suspend();
        StopStream();
    }

    private void StopStream()
    {
        _streaming?.Cancel();
        _streaming?.Dispose();
        _streaming = null;
        IsStreaming = false;
    }

    protected override async Task RefreshAsync()
    {
        foreach (var row in Devices_)
        {
            var status = await System.AskAsync<DeviceStatus>(
                ActorId.For<DeviceActor>(row.Id), new GetDeviceStatus(), TimeSpan.FromSeconds(10));

            row.Latest = status.Latest;
            row.Average = status.Average;
            row.Minimum = status.Minimum;
            row.Maximum = status.Maximum;
            row.Readings = status.Readings;
            row.InAlarm = status.InAlarm;
            row.Alarms = status.AlarmsRaised;
        }

        var alarms = await System.AskAsync<ActiveAlarms>(
            ActorId.For<AlarmDeskActor>("main"), new GetActiveAlarms(), TimeSpan.FromSeconds(10));

        ActiveAlarms = alarms.Devices.Count == 0
            ? $"none ({alarms.RaisedTotal} raised so far)"
            : string.Join(", ", alarms.Devices);
    }
}
