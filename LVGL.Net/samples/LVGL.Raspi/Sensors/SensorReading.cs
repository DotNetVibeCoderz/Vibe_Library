namespace Lvgl.Samples.Raspi.Sensors;

/// <summary>One sample of the board's vital signs.</summary>
/// <param name="CpuLoadPercent">Aggregate CPU utilisation since the previous sample, 0-100.</param>
/// <param name="CpuTemperatureC">SoC temperature in degrees Celsius.</param>
/// <param name="MemoryUsedPercent">Fraction of RAM in use, 0-100.</param>
/// <param name="MemoryTotalMb">Total RAM in mebibytes.</param>
/// <param name="Uptime">How long the board has been running.</param>
public readonly record struct SensorReading(
    double CpuLoadPercent,
    double CpuTemperatureC,
    double MemoryUsedPercent,
    double MemoryTotalMb,
    TimeSpan Uptime);

/// <summary>A pollable source of board telemetry.</summary>
public interface ISensorSource : IDisposable
{
    /// <summary>Human-readable name of where the data comes from, shown in the UI.</summary>
    string Description { get; }

    /// <summary>
    /// Takes a sample. Called from a background thread, never from the LVGL thread, because some
    /// of these reads touch the filesystem.
    /// </summary>
    SensorReading Read();
}
