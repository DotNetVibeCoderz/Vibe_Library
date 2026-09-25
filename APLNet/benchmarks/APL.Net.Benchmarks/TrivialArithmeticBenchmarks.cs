// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;

namespace AplNet.Benchmarks;

/// <summary>
/// B1: <c>x * 2 + 1</c> (~1 ns per element). Dispatch overhead dominates in cache; at 100M elements
/// (1.6 GB of traffic per call) memory bandwidth does, and every parallel variant converges.
/// </summary>
public class TrivialArithmeticBenchmarks
{
    private double[] _source = [];
    private double[] _destination = [];

    [Params(10_000, 1_000_000, 100_000_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _source = new double[N];
        _destination = new double[N];
        var random = new Random(42);
        for (int i = 0; i < N; i++)
            _source[i] = random.NextDouble();
    }

    [Benchmark(Baseline = true)]
    public void Sequential()
    {
        double[] s = _source, d = _destination;
        for (int i = 0; i < s.Length; i++)
            d[i] = Workloads.Trivial(s[i]);
    }

    [Benchmark]
    public void ParallelFor()
    {
        double[] s = _source, d = _destination;
        Parallel.For(0, s.Length, i => d[i] = Workloads.Trivial(s[i]));
    }

    /// <summary>TPL as an expert would write it: range partitions, a plain loop inside each.</summary>
    [Benchmark]
    public void ParallelForEachRangePartitioner()
    {
        double[] s = _source, d = _destination;
        Parallel.ForEach(Partitioner.Create(0, s.Length), range =>
        {
            for (int i = range.Item1; i < range.Item2; i++)
                d[i] = Workloads.Trivial(s[i]);
        });
    }

    [Benchmark]
    public void AplForDelegate()
    {
        double[] s = _source, d = _destination;
        Apl.For(0, s.Length, i => d[i] = Workloads.Trivial(s[i]));
    }

    [Benchmark]
    public void AplForStruct() => Apl.For(0, _source.Length, new TrivialBody(_source, _destination));

    [Benchmark]
    public void AplForRangeStruct() => Apl.ForRange(0, _source.Length, new TrivialRangeBody(_source, _destination));
}
