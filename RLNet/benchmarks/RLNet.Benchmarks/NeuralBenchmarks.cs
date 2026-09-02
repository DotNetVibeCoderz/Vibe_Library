// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using BenchmarkDotNet.Attributes;
using RLNet.Neural;
using RLNet.Utils;

namespace RLNet.Benchmarks;

/// <summary>
/// Times the network operations a training run spends most of its time inside.
/// </summary>
/// <remarks>
/// The memory column is the one to read first. A forward-backward-update cycle here should
/// allocate exactly zero bytes; anything else means a buffer is being created per call, and at a
/// million gradient steps that is the difference between a training run and a garbage collector
/// benchmark.
/// </remarks>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class NeuralBenchmarks
{
    private MlpNetwork _network = null!;
    private AdamOptimizer _optimizer = null!;
    private float[] _input = null!;

    /// <summary>Batch size. 1 is action selection; 64 and 256 are the agents' gradient steps.</summary>
    [Params(1, 64, 256)]
    public int Batch { get; set; }

    /// <summary>Hidden width. 64 is PPO's default, 256 SAC's and TD3's.</summary>
    [Params(64, 256)]
    public int Width { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new FastRandom(1);
        _network = new MlpNetwork(16, [Width, Width], 4, Activation.ReLU, Activation.Linear, Batch, random);
        _optimizer = new AdamOptimizer(_network.ParameterCount, 3e-4f);

        _input = new float[Batch * 16];
        for (int i = 0; i < _input.Length; i++) _input[i] = random.NextGaussian();
    }

    [Benchmark(Baseline = true, Description = "Forward")]
    public float Forward()
    {
        _input.CopyTo(_network.InputBuffer(Batch));
        return _network.Forward(Batch)[0];
    }

    [Benchmark(Description = "Forward + backward")]
    public void ForwardBackward()
    {
        _input.CopyTo(_network.InputBuffer(Batch));
        var output = _network.Forward(Batch);

        var gradient = _network.OutputGradientBuffer(Batch);
        for (int i = 0; i < gradient.Length; i++) gradient[i] = output[i] * 0.01f;

        _network.ZeroGradients();
        _network.Backward(Batch);
    }

    /// <summary>The complete gradient step, which is what an agent actually does per update.</summary>
    [Benchmark(Description = "Full gradient step")]
    public void GradientStep()
    {
        _input.CopyTo(_network.InputBuffer(Batch));
        var output = _network.Forward(Batch);

        var gradient = _network.OutputGradientBuffer(Batch);
        for (int i = 0; i < gradient.Length; i++) gradient[i] = output[i] * 0.01f;

        _network.ZeroGradients();
        _network.Backward(Batch);
        _network.ApplyGradients(_optimizer, 1f / Batch);
    }
}

/// <summary>
/// Isolates the SIMD primitives against straightforward scalar loops.
/// </summary>
/// <remarks>
/// The scalar variants are here as a control, not as a strawman: they are what the same code
/// looks like written the obvious way. The gap between them is the entire justification for
/// <see cref="SimdOps"/> existing, so it is worth being able to measure rather than assert.
/// </remarks>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class SimdBenchmarks
{
    private float[] _a = null!;
    private float[] _b = null!;

    /// <summary>Vector length. 256 is one layer's width; 65536 is a whole weight matrix.</summary>
    [Params(256, 4096, 65536)]
    public int Length { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new FastRandom(2);
        _a = new float[Length];
        _b = new float[Length];
        for (int i = 0; i < Length; i++)
        {
            _a[i] = random.NextGaussian();
            _b[i] = random.NextGaussian();
        }
    }

    [Benchmark(Baseline = true, Description = "Dot (scalar)")]
    public float DotScalar()
    {
        float sum = 0f;
        for (int i = 0; i < _a.Length; i++) sum += _a[i] * _b[i];
        return sum;
    }

    [Benchmark(Description = "Dot (SIMD)")]
    public float DotSimd() => SimdOps.Dot(_a, _b);

    [Benchmark(Description = "AddScaled (scalar)")]
    public void AddScaledScalar()
    {
        for (int i = 0; i < _a.Length; i++) _a[i] += 0.5f * _b[i];
    }

    [Benchmark(Description = "AddScaled (SIMD)")]
    public void AddScaledSimd() => SimdOps.AddScaled(_a, _b, 0.5f);

    [Benchmark(Description = "Polyak blend (SIMD)")]
    public void PolyakSimd() => SimdOps.PolyakBlend(_a, _b, 0.005f);
}
