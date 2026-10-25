using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lvgl.Samples.Raspi.Sensors;

namespace Lvgl.Samples.Raspi;

/// <summary>
/// Drives the dashboard flat out for a fixed time and reports how it performed - the measurement
/// behind the "light enough for a Pi" claim, rather than an impression of it.
/// </summary>
/// <remarks>
/// <para>
/// Sensor samples arrive every 50 ms instead of every second, so the chart, gauges and labels
/// redraw continuously: the bench measures rendering under load, not an idle screen. Frame time is
/// the time spent in <see cref="LvglApplication.RunFrame"/> - timers, layout, rendering and the
/// blit - with the idle sleep excluded.
/// </para>
/// <para>
/// Allocation is measured on the UI thread across the whole run and divided by frames. The render
/// path is meant to allocate nothing per frame; what remains is the dashboard's own string
/// formatting for its labels.
/// </para>
/// </remarks>
internal static class Bench
{
    public static BenchReport Run(LvglApplication application, Dashboard dashboard, ISensorSource sensors, TimeSpan duration, string backendName)
    {
        var frameTimes = new List<double>(capacity: 20_000);
        var clock = Stopwatch.StartNew();
        var nextSample = TimeSpan.Zero;

        // Warm up: JIT, first layout and font caches would otherwise dominate the first frames.
        for (var i = 0; i < 30; i++) application.RunFrame();

        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var gen0Before = GC.CollectionCount(0);
        clock.Restart();

        var frame = Stopwatch.StartNew();
        while (clock.Elapsed < duration)
        {
            if (clock.Elapsed >= nextSample)
            {
                dashboard.Update(sensors.Read());
                nextSample = clock.Elapsed + TimeSpan.FromMilliseconds(50);
            }

            frame.Restart();
            application.RunFrame();
            frameTimes.Add(frame.Elapsed.TotalMilliseconds);

            var idle = Math.Clamp(application.NextIdleMs, 0, 5);
            if (idle > 0) Thread.Sleep(idle);
        }

        var elapsed = clock.Elapsed;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        frameTimes.Sort();
        double Percentile(double p) => frameTimes.Count == 0 ? 0 : frameTimes[Math.Min(frameTimes.Count - 1, (int)(p * frameTimes.Count))];

        using var process = Process.GetCurrentProcess();

        return new BenchReport
        {
            Processors = Environment.ProcessorCount,
            Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Lvgl = LvglRuntime.LvglVersion.ToString(3),
            Backend = backendName,
            Width = application.Backend.Width,
            Height = application.Backend.Height,
            Seconds = Math.Round(elapsed.TotalSeconds, 2),
            Frames = frameTimes.Count,
            FramesPerSecond = Math.Round(frameTimes.Count / elapsed.TotalSeconds, 1),
            FrameMsMedian = Math.Round(Percentile(0.50), 3),
            FrameMsP95 = Math.Round(Percentile(0.95), 3),
            FrameMsP99 = Math.Round(Percentile(0.99), 3),
            FrameMsMax = Math.Round(frameTimes.Count == 0 ? 0 : frameTimes[^1], 3),
            BytesAllocatedPerFrame = frameTimes.Count == 0 ? 0 : Math.Round((double)allocated / frameTimes.Count, 1),
            Gen0Collections = GC.CollectionCount(0) - gen0Before,
            DrawBufferKiB = application.Display.AllocatedBytes / 1024,
            WorkingSetMiB = Math.Round(process.WorkingSet64 / 1048576.0, 1),
            ManagedHeapMiB = Math.Round(GC.GetTotalMemory(forceFullCollection: false) / 1048576.0, 1),
        };
    }

    public static string ToJson(BenchReport report) => JsonSerializer.Serialize(report, BenchJson.Default.BenchReport);
}

/// <summary>One bench run, as written to the report file.</summary>
internal sealed record BenchReport
{
    public int Processors { get; init; }
    public string Os { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string Runtime { get; init; } = "";
    public string Lvgl { get; init; } = "";
    public string Backend { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public double Seconds { get; init; }
    public int Frames { get; init; }
    public double FramesPerSecond { get; init; }
    public double FrameMsMedian { get; init; }
    public double FrameMsP95 { get; init; }
    public double FrameMsP99 { get; init; }
    public double FrameMsMax { get; init; }
    public double BytesAllocatedPerFrame { get; init; }
    public int Gen0Collections { get; init; }
    public long DrawBufferKiB { get; init; }
    public double WorkingSetMiB { get; init; }
    public double ManagedHeapMiB { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BenchReport))]
internal sealed partial class BenchJson : JsonSerializerContext;
