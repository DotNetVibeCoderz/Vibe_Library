namespace Lvgl.Samples.Raspi.Sensors;

/// <summary>
/// Plausible fake telemetry, so the dashboard can be developed and demonstrated on a machine that
/// is not a Raspberry Pi.
/// </summary>
public sealed class SimulatedSensorSource : ISensorSource
{
    private readonly Random _random = new(7);
    private readonly DateTime _started = DateTime.UtcNow;
    private double _phase;
    private double _load = 25;
    private double _temperature = 46;

    public string Description => "Simulated sensors (not running on a Pi)";

    public SensorReading Read()
    {
        _phase += 0.15;

        // A slow sine plus a random walk reads like real load: mostly smooth, occasionally spiky.
        var target = 35 + Math.Sin(_phase) * 22 + _random.Next(-6, 7);
        _load = Math.Clamp(_load + (target - _load) * 0.35, 0, 100);

        // Temperature lags load, as it does on real silicon.
        var targetTemperature = 42 + _load * 0.28;
        _temperature += (targetTemperature - _temperature) * 0.08;

        return new SensorReading(
            _load,
            _temperature,
            48 + Math.Sin(_phase * 0.4) * 8,
            3840,
            DateTime.UtcNow - _started);
    }

    public void Dispose() { }
}
