// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.Intrinsics;
using BenchmarkDotNet.Attributes;
using AplNet.Simd;

namespace AplNet.Benchmarks;

/// <summary>
/// B4: AXPY-style <c>x = x * a + b</c> in place over floats. With a = 0.5 and b = 1 the values settle at
/// 2 rather than growing towards infinity or denormals, which would change the cost per element.
/// </summary>
public class SimdBenchmarks
{
    private const float A = 0.5f;
    private const float B = 1f;

    private float[] _data = [];

    [Params(1_000_000, 10_000_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new float[N];
        var random = new Random(7);
        for (int i = 0; i < N; i++)
            _data[i] = (float)random.NextDouble();
    }

    [Benchmark(Baseline = true)]
    public void ScalarSequential()
    {
        float[] data = _data;
        for (int i = 0; i < data.Length; i++)
            data[i] = data[i] * A + B;
    }

    [Benchmark]
    public void ParallelFor()
    {
        float[] data = _data;
        Parallel.For(0, data.Length, i => data[i] = data[i] * A + B);
    }

    [Benchmark]
    public void AplForStructScalar() => Apl.For(0, _data.Length, new AxpyScalarBody(_data, A, B));

    [Benchmark]
    public void AplSimdSingleThread() => SimdOps.TransformInPlace(_data.AsSpan(), new MultiplyAddOperator<float>(A, B));

    [Benchmark]
    public void AplParallelSimd() => SimdOps.ParallelTransformInPlace(_data, new MultiplyAddOperator<float>(A, B));

    [Benchmark]
    public void AplParallelSimdDelegates() =>
        SimdOps.ParallelTransformInPlace(_data, v => v * Vector256.Create(A) + Vector256.Create(B), x => x * A + B);
}
