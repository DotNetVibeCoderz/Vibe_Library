// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using BenchmarkDotNet.Attributes;
using AplNet.Core.Partitioners;

namespace AplNet.Benchmarks;

/// <summary>
/// B6: 10K items whose cost follows Zipf's law (item of rank r costs ~2,000,000 / r multiply-adds; the
/// most expensive single item is about a tenth of all the work). This benchmark exists to show where
/// static partitioning loses, not to hide it.
/// </summary>
/// <remarks>
/// <c>Sorted</c> puts the heavy items first, so the first static partition owns ~78% of the work -
/// the worst case. <c>Shuffled</c> scatters them at random, the more common shape in practice.
/// </remarks>
public class UnevenWorkloadBenchmarks
{
    public enum CostLayout
    {
        Sorted,
        Shuffled,
    }

    private static readonly AplOptions s_workStealing = new() { Partitioner = new WorkStealingPartitioner(1) };
    private static readonly AplOptions s_striped = new() { Partitioner = new StripedPartitioner(16) };

    private int[] _cost = [];
    private double[] _results = [];

    [Params(10_000)]
    public int N { get; set; }

    [Params(CostLayout.Sorted, CostLayout.Shuffled)]
    public CostLayout Layout { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _cost = Enumerable.Range(1, N).Select(rank => 2_000_000 / rank).ToArray();
        if (Layout == CostLayout.Shuffled)
            new Random(3).Shuffle(_cost);
        _results = new double[N];
    }

    [Benchmark(Baseline = true)]
    public void Sequential()
    {
        for (int i = 0; i < _cost.Length; i++)
            _results[i] = Workloads.Spin(_cost[i], i);
    }

    [Benchmark]
    public void ParallelFor()
    {
        int[] cost = _cost;
        double[] results = _results;
        Parallel.For(0, cost.Length, i => results[i] = Workloads.Spin(cost[i], i));
    }

    [Benchmark]
    public void AplStatic() => Apl.For(0, _cost.Length, new SpinBody(_cost, _results));

    [Benchmark]
    public void AplStriped() => Apl.For(0, _cost.Length, new SpinBody(_cost, _results), s_striped);

    [Benchmark]
    public void AplWorkStealing() => Apl.For(0, _cost.Length, new SpinBody(_cost, _results), s_workStealing);
}
