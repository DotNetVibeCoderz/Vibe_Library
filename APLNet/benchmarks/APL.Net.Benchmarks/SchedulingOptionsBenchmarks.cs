// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using BenchmarkDotNet.Attributes;
using AplNet.Core;
using AplNet.Core.Partitioners;
using AplNet.Simd;

namespace AplNet.Benchmarks;

/// <summary>
/// B8: the per-call cost of tiny loops on persistent spinning workers versus the thread pool. The
/// same loops as B7. BenchmarkDotNet calls back to back, so the workers are always still spinning
/// when the next call arrives. That is the best case, and the case the option is for: a game or
/// simulation tick that issues loops in quick succession.
/// </summary>
public class SpinningWorkersBenchmarks
{
    private double[] _source = [];
    private double[] _destination = [];
    private SpinningWorkers? _workers;
    private AplOptions _spinning = AplOptions.Default;
    private AplOptions _pinned = AplOptions.Default;
    private SpinningWorkers? _pinnedWorkers;

    [Params(1_000, 16_000)]
    public int N { get; set; }

    // Each benchmark runs in its own process; only the spinning ones start worker threads, so the
    // others measure a machine with nothing extra on it.
    [GlobalSetup(Targets = [nameof(ParallelFor), nameof(AplForStructPool), nameof(Sequential)])]
    public void Setup()
    {
        _source = Enumerable.Range(0, N).Select(i => (double)i).ToArray();
        _destination = new double[N];
    }

    [GlobalSetup(Target = nameof(AplForStructSpinning))]
    public void SetupSpinning()
    {
        Setup();
        _workers = new SpinningWorkers();
        _spinning = new AplOptions { Workers = _workers };
    }

    [GlobalSetup(Target = nameof(AplForStructSpinningPinned))]
    public void SetupPinned()
    {
        Setup();
        _pinnedWorkers = new SpinningWorkers(affinity: WorkerAffinity.Core);
        _pinned = new AplOptions { Workers = _pinnedWorkers };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _workers?.Dispose();
        _pinnedWorkers?.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void ParallelFor()
    {
        double[] s = _source, d = _destination;
        Parallel.For(0, s.Length, i => d[i] = Workloads.Trivial(s[i]));
    }

    [Benchmark]
    public void AplForStructPool() => Apl.For(0, _source.Length, new TrivialBody(_source, _destination));

    [Benchmark]
    public void AplForStructSpinning() => Apl.For(0, _source.Length, new TrivialBody(_source, _destination), _spinning);

    [Benchmark]
    public void AplForStructSpinningPinned() => Apl.For(0, _source.Length, new TrivialBody(_source, _destination), _pinned);

    [Benchmark]
    public void Sequential()
    {
        double[] s = _source, d = _destination;
        for (int i = 0; i < s.Length; i++)
            d[i] = Workloads.Trivial(s[i]);
    }
}

/// <summary>
/// B9: straggler tolerance. One background thread per run keeps a core busy, as another process
/// would, so one of the loop's workers shares its core and finishes late. One static partition per
/// worker waits for that worker; four per worker let the others take its leftovers. Work stealing
/// and <c>Parallel.For</c> balance dynamically, so they are the reference points. The work is
/// uniform, which is what static partitioning is the default for.
/// </summary>
public class StragglerBenchmarks
{
    private double[] _source = [];
    private double[] _destination = [];
    private Thread? _hog;
    private volatile bool _stop;
    private readonly AplOptions _static = new() { Partitioner = StaticRangePartitioner.Instance };
    private readonly AplOptions _static4 = new() { Partitioner = new StaticRangePartitioner(partitionsPerWorker: 4) };
    private readonly AplOptions _stealing = new() { Partitioner = WorkStealingPartitioner.Instance };

    [Params(false, true)]
    public bool BusyCore { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _source = Enumerable.Range(0, 1_000_000).Select(i => (double)i).ToArray();
        _destination = new double[_source.Length];
        if (BusyCore)
        {
            _stop = false;
            _hog = new Thread(() =>
            {
                double x = 1;
                while (!_stop)
                    x = Workloads.Spin(10_000, x);
                GC.KeepAlive(x);
            }) { IsBackground = true, Name = "B9 hog" };
            _hog.Start();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _stop = true;
        _hog?.Join();
    }

    [Benchmark(Baseline = true)]
    public void ParallelFor()
    {
        double[] s = _source, d = _destination;
        Parallel.For(0, s.Length, i => d[i] = Workloads.Medium(s[i]));
    }

    [Benchmark]
    public void AplStatic() => Apl.For(0, _source.Length, new MediumBody(_source, _destination), _static);

    [Benchmark]
    public void AplStatic4PerWorker() => Apl.For(0, _source.Length, new MediumBody(_source, _destination), _static4);

    [Benchmark]
    public void AplWorkStealing() => Apl.For(0, _source.Length, new MediumBody(_source, _destination), _stealing);
}

/// <summary>
/// B10: the 1.1 SIMD operators over 1M doubles: <c>Exp</c> and <c>Log</c> against the scalar
/// <see cref="Math"/> functions, and the fused multiply-add against the two-rounding multiply-add.
/// </summary>
public class SimdMathBenchmarks
{
    private double[] _source = [];
    private double[] _destination = [];

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(11);
        _source = Enumerable.Range(0, 1_000_000).Select(_ => random.NextDouble() * 20 + 0.001).ToArray();
        _destination = new double[_source.Length];
    }

    [Benchmark(Baseline = true)]
    public void ExpScalar()
    {
        double[] s = _source, d = _destination;
        for (int i = 0; i < s.Length; i++)
            d[i] = Math.Exp(s[i]);
    }

    [Benchmark]
    public void ExpSimd() => SimdOps.Transform<double, ExpOperator<double>>(_source, _destination, default);

    [Benchmark]
    public void ExpParallelSimd() => SimdOps.ParallelTransform(_source, _destination, new ExpOperator<double>());

    [Benchmark]
    public void LogScalar()
    {
        double[] s = _source, d = _destination;
        for (int i = 0; i < s.Length; i++)
            d[i] = Math.Log(s[i]);
    }

    [Benchmark]
    public void LogSimd() => SimdOps.Transform<double, LogOperator<double>>(_source, _destination, default);

    [Benchmark]
    public void MultiplyAddSimd() => SimdOps.Transform(_source, _destination, new MultiplyAddOperator<double>(1.5, 0.25));

    [Benchmark]
    public void FusedMultiplyAddSimd() => SimdOps.Transform(_source, _destination, new FusedMultiplyAddOperator<double>(1.5, 0.25));
}
