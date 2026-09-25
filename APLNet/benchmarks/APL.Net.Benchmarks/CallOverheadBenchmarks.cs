// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using BenchmarkDotNet.Attributes;

namespace AplNet.Benchmarks;

/// <summary>
/// B7: the fixed cost of one parallel call and what it allocates. The loop is tiny on purpose so the
/// time and the "Allocated" column are almost entirely scheduling: waking workers, handing out work,
/// joining.
/// </summary>
public class CallOverheadBenchmarks
{
    private double[] _source = [];
    private double[] _destination = [];

    [Params(1_000, 16_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _source = Enumerable.Range(0, N).Select(i => (double)i).ToArray();
        _destination = new double[N];
    }

    [Benchmark(Baseline = true)]
    public void ParallelFor()
    {
        double[] s = _source, d = _destination;
        Parallel.For(0, s.Length, i => d[i] = Workloads.Trivial(s[i]));
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
    public void Sequential()
    {
        double[] s = _source, d = _destination;
        for (int i = 0; i < s.Length; i++)
            d[i] = Workloads.Trivial(s[i]);
    }
}
