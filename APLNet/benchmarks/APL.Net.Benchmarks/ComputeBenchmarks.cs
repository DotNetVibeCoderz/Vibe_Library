// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using BenchmarkDotNet.Attributes;

namespace AplNet.Benchmarks;

/// <summary>B2: <c>sqrt(x) * sin(x)</c> (~20-50 ns per element), 1M elements.</summary>
public class MediumComputeBenchmarks
{
    private double[] _source = [];
    private double[] _destination = [];

    [Params(1_000_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _source = Enumerable.Range(0, N).Select(i => i * 0.001).ToArray();
        _destination = new double[N];
    }

    [Benchmark(Baseline = true)]
    public void Sequential()
    {
        double[] s = _source, d = _destination;
        for (int i = 0; i < s.Length; i++)
            d[i] = Workloads.Medium(s[i]);
    }

    [Benchmark]
    public void ParallelFor()
    {
        double[] s = _source, d = _destination;
        Parallel.For(0, s.Length, i => d[i] = Workloads.Medium(s[i]));
    }

    [Benchmark]
    public void AplForDelegate()
    {
        double[] s = _source, d = _destination;
        Apl.For(0, s.Length, i => d[i] = Workloads.Medium(s[i]));
    }

    [Benchmark]
    public void AplForStruct() => Apl.For(0, _source.Length, new MediumBody(_source, _destination));
}

/// <summary>B3: ~500 ns of arithmetic per element (a 4x4 matrix power), 100K elements.</summary>
public class HeavyComputeBenchmarks
{
    private double[] _source = [];
    private double[] _destination = [];

    [Params(100_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _source = Enumerable.Range(0, N).Select(i => (double)i).ToArray();
        _destination = new double[N];
    }

    [Benchmark(Baseline = true)]
    public void Sequential()
    {
        double[] s = _source, d = _destination;
        for (int i = 0; i < s.Length; i++)
            d[i] = Workloads.Heavy(s[i]);
    }

    [Benchmark]
    public void ParallelFor()
    {
        double[] s = _source, d = _destination;
        Parallel.For(0, s.Length, i => d[i] = Workloads.Heavy(s[i]));
    }

    [Benchmark]
    public void AplForDelegate()
    {
        double[] s = _source, d = _destination;
        Apl.For(0, s.Length, i => d[i] = Workloads.Heavy(s[i]));
    }

    [Benchmark]
    public void AplForStruct() => Apl.For(0, _source.Length, new HeavyBody(_source, _destination));
}
