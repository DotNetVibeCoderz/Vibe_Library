// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using BenchmarkDotNet.Attributes;
using AplNet.Simd;

namespace AplNet.Benchmarks;

/// <summary>B5: the sum of an array of doubles.</summary>
public class ReductionBenchmarks
{
    private double[] _data = [];

    [Params(1_000_000, 10_000_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new double[N];
        var random = new Random(11);
        for (int i = 0; i < N; i++)
            _data[i] = random.NextDouble();
    }

    [Benchmark(Baseline = true)]
    public double LinqSum() => _data.Sum();

    [Benchmark]
    public double SequentialLoop()
    {
        double sum = 0;
        foreach (double v in _data)
            sum += v;
        return sum;
    }

    /// <summary>TPL's recommended shape: a thread-local subtotal per task, merged under a lock at the end.</summary>
    [Benchmark]
    public double ParallelForLocalSubtotals()
    {
        double[] data = _data;
        double total = 0;
        object gate = new();
        Parallel.For(0, data.Length, () => 0.0, (i, _, local) => local + data[i], local =>
        {
            lock (gate)
                total += local;
        });
        return total;
    }

    [Benchmark]
    public double AplSimdSingleThread() => SimdOps.Sum<double>(_data);

    [Benchmark]
    public double AplParallelSimd() => SimdOps.ParallelSum(_data);
}
