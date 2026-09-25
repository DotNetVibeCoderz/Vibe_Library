// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics;

namespace AplNet.Gallery.Infrastructure;

/// <summary>A variant's timing: the median of many runs, which shrugs off the odd descheduled one.</summary>
public sealed record Measurement(string Name, VariantKind Kind, double MedianMs, double BestMs, long BytesPerRun, int Runs);

/// <summary>
/// A small in-app benchmark harness. Not BenchmarkDotNet - it has to finish in a second or two per
/// variant while someone watches - but it follows the same rules: warm up until the JIT has settled,
/// collect garbage between variants, time many runs and report the median.
/// </summary>
public static class BenchmarkRunner
{
    public static Measurement Measure(Variant variant, TimeSpan budget, CancellationToken token)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Warm-up: enough runs for tiered compilation to promote the hot loop (it needs ~30 calls,
        // or a loop that runs long enough for OSR), bounded in time for the slow variants.
        var warm = Stopwatch.StartNew();
        for (int i = 0; i < 40 && (i < 3 || warm.Elapsed < budget / 3); i++)
        {
            token.ThrowIfCancellationRequested();
            variant.Run();
        }

        var samples = new List<double>(64);
        var total = Stopwatch.StartNew();
        while (samples.Count < 5 || (total.Elapsed < budget && samples.Count < 200))
        {
            token.ThrowIfCancellationRequested();
            long start = Stopwatch.GetTimestamp();
            variant.Run();
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        samples.Sort();
        return new Measurement(variant.Name, variant.Kind, samples[samples.Count / 2], samples[0], -1, samples.Count);
    }

    /// <summary>
    /// Bytes allocated per run, process-wide (pool workers allocate on their own threads). Taken in a
    /// separate pass once the interface has stopped animating: the render loop allocates too, and
    /// timing runs overlap the bars growing.
    /// </summary>
    public static long MeasureAllocations(Variant variant, int runs)
    {
        variant.Run();
        long before = GC.GetTotalAllocatedBytes(precise: true);
        for (int i = 0; i < runs; i++)
            variant.Run();
        return (GC.GetTotalAllocatedBytes(precise: true) - before) / runs;
    }
}

/// <summary>Collects (thread, start, end) spans from a traced run, to draw one lane per thread.</summary>
public sealed class LaneRecorder
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(int Thread, long Start, long End)> _spans = new();
    private long _origin = Stopwatch.GetTimestamp();

    public void Restart()
    {
        _spans.Clear();
        _origin = Stopwatch.GetTimestamp();
    }

    public void Record(long start, long end) => _spans.Enqueue((Environment.CurrentManagedThreadId, start, end));

    /// <summary>Spans per thread in milliseconds from the start, lanes ordered by when each thread first worked.</summary>
    public LaneTrace ToTrace(string name, VariantKind kind)
    {
        double toMs = 1000.0 / Stopwatch.Frequency;
        var lanes = _spans
            .GroupBy(s => s.Thread)
            .Select(g => Merge(g.Select(s => ((s.Start - _origin) * toMs, (s.End - _origin) * toMs)).OrderBy(s => s.Item1).ToList()))
            .OrderBy(l => l[0].Start)
            .ToList();
        double end = lanes.Count == 0 ? 0 : lanes.Max(l => l[^1].End);
        return new LaneTrace(name, kind, lanes, end);
    }

    /// <summary>Joins spans closer together than a few microseconds - consecutive iterations on one thread.</summary>
    private static List<(double Start, double End)> Merge(List<(double Start, double End)> spans)
    {
        var merged = new List<(double Start, double End)>();
        foreach (var span in spans)
        {
            if (merged.Count > 0 && span.Start - merged[^1].End < 0.02)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, span.End));
            else
                merged.Add(span);
        }

        return merged;
    }
}

public sealed record LaneTrace(string Name, VariantKind Kind, IReadOnlyList<List<(double Start, double End)>> Lanes, double TotalMs);
