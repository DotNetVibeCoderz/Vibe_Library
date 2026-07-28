using System.Globalization;

namespace Lvgl.Samples.Raspi.Sensors;

/// <summary>
/// Reads a Raspberry Pi's built-in sensors through the kernel's sysfs and procfs interfaces.
/// </summary>
/// <remarks>
/// No extra packages are needed: the SoC temperature, CPU utilisation and memory pressure are all
/// exposed as text files. CPU load in particular is only meaningful as a difference between two
/// samples, so the first reading reports zero and later ones compare against the previous totals.
/// </remarks>
public sealed class LinuxSensorSource : ISensorSource
{
    private const string ThermalPath = "/sys/class/thermal/thermal_zone0/temp";
    private const string StatPath = "/proc/stat";
    private const string MemInfoPath = "/proc/meminfo";

    private ulong _previousIdle;
    private ulong _previousTotal;

    /// <summary>True when this machine exposes the files the source needs.</summary>
    public static bool IsAvailable => OperatingSystem.IsLinux() && File.Exists(StatPath);

    public string Description => File.Exists(ThermalPath)
        ? "Raspberry Pi onboard sensors (sysfs/procfs)"
        : "Linux procfs (no thermal zone found)";

    public SensorReading Read()
    {
        var (used, total) = ReadMemory();

        return new SensorReading(
            ReadCpuLoad(),
            ReadTemperature(),
            total > 0 ? used / total * 100.0 : 0,
            total,
            ReadUptime());
    }

    private double ReadTemperature()
    {
        // The thermal zone reports millidegrees Celsius as a single integer.
        if (!TryReadAllText(ThermalPath, out var text)) return double.NaN;

        return long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var milli)
            ? milli / 1000.0
            : double.NaN;
    }

    private double ReadCpuLoad()
    {
        if (!TryReadFirstLine(StatPath, out var line) || !line.StartsWith("cpu ", StringComparison.Ordinal))
        {
            return 0;
        }

        // "cpu  user nice system idle iowait irq softirq steal guest guest_nice"
        var fields = line.AsSpan(4);
        ulong total = 0;
        ulong idle = 0;
        var index = 0;

        foreach (var range in fields.Split(' '))
        {
            var token = fields[range].Trim();
            if (token.IsEmpty) continue;

            if (!ulong.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) continue;

            total += value;
            if (index is 3 or 4) idle += value;   // idle and iowait both count as not-working
            index++;
        }

        var totalDelta = total - _previousTotal;
        var idleDelta = idle - _previousIdle;

        _previousTotal = total;
        _previousIdle = idle;

        // The first call has no baseline to compare against.
        if (totalDelta == 0) return 0;

        var busy = (double)(totalDelta - idleDelta) / totalDelta * 100.0;
        return Math.Clamp(busy, 0, 100);
    }

    private static (double UsedMb, double TotalMb) ReadMemory()
    {
        if (!TryReadAllLines(MemInfoPath, out var lines)) return (0, 0);

        double total = 0;
        double available = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal)) total = ParseKilobytes(line);
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal)) available = ParseKilobytes(line);

            if (total > 0 && available > 0) break;
        }

        var totalMb = total / 1024.0;
        var availableMb = available / 1024.0;
        return (Math.Max(0, totalMb - availableMb), totalMb);
    }

    private static double ParseKilobytes(string line)
    {
        // "MemTotal:        3885396 kB"
        var span = line.AsSpan();
        var colon = span.IndexOf(':');
        if (colon < 0) return 0;

        var rest = span[(colon + 1)..].Trim();
        var space = rest.IndexOf(' ');
        if (space > 0) rest = rest[..space];

        return double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static TimeSpan ReadUptime()
    {
        if (!TryReadFirstLine("/proc/uptime", out var line)) return TimeSpan.Zero;

        var space = line.IndexOf(' ');
        var seconds = space > 0 ? line[..space] : line;

        return double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? TimeSpan.FromSeconds(value)
            : TimeSpan.Zero;
    }

    // These files can vanish or briefly fail to read on a busy system; a missing sample should
    // degrade the display, not take the dashboard down.
    private static bool TryReadAllText(string path, out string text)
    {
        try
        {
            text = File.ReadAllText(path);
            return true;
        }
        catch (IOException)
        {
            text = string.Empty;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            text = string.Empty;
            return false;
        }
    }

    private static bool TryReadFirstLine(string path, out string line)
    {
        if (TryReadAllText(path, out var text))
        {
            var newline = text.IndexOf('\n');
            line = newline >= 0 ? text[..newline] : text;
            return true;
        }

        line = string.Empty;
        return false;
    }

    private static bool TryReadAllLines(string path, out string[] lines)
    {
        if (TryReadAllText(path, out var text))
        {
            lines = text.Split('\n');
            return true;
        }

        lines = [];
        return false;
    }

    public void Dispose() { }
}
