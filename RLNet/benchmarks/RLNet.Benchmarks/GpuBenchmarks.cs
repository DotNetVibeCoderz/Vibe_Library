// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using BenchmarkDotNet.Attributes;
using RLNet.Gpu;
using RLNet.Neural;
using RLNet.Utils;

namespace RLNet.Benchmarks;

/// <summary>
/// Finds the size at which the GPU backend starts beating the CPU one.
/// </summary>
/// <remarks>
/// <para>
/// The claim in the documentation is that GPU is the wrong default for classic control and the
/// right choice for wide networks. This is the measurement behind it, and it is parameterised
/// across the range so the crossover can be read off rather than asserted.
/// </para>
/// <para>
/// The GPU rows fall back to the CPU path below <see cref="GpuComputeBackend.MinimumWorkPerCall"/>,
/// so that threshold is lowered to zero here. Leaving it in place would have the small cases
/// measure the CPU twice and hide exactly the effect being looked for.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class GpuBenchmarks : IDisposable
{
    private IComputeBackend _cpu = null!;
    private IComputeBackend _gpu = null!;

    private float[] _weights = null!;
    private float[] _biases = null!;
    private float[] _input = null!;
    private float[] _output = null!;

    /// <summary>Hidden width. 64 is PPO's default; 1024 is far past anything classic control needs.</summary>
    [Params(64, 256, 1024)]
    public int Width { get; set; }

    [Params(256)]
    public int Batch { get; set; }

    /// <summary>
    /// The device the GPU rows actually ran on, shown as a column.
    /// </summary>
    /// <remarks>
    /// A column rather than a <c>Console.WriteLine</c> in <see cref="Setup"/>: BenchmarkDotNet
    /// talks to its child process over stdout, so printing from inside a benchmark corrupts that
    /// protocol and every row comes back as NA. Reporting through the summary is both correct and
    /// more useful — the device name sits beside the numbers it produced.
    /// </remarks>
    [ParamsSource(nameof(BackendNames))]
    public string Device { get; set; } = "";

    /// <summary>Resolved once, so the summary names the real device rather than a guess.</summary>
    public static IEnumerable<string> BackendNames()
    {
        using var probe = GpuComputeBackend.TryCreate();
        yield return probe.IsAccelerated ? probe.Name : $"{probe.Name} (no accelerator - GPU rows are CPU)";
    }

    [GlobalSetup]
    public void Setup()
    {
        _cpu = CpuComputeBackend.Instance;

        var gpu = GpuComputeBackend.TryCreate();

        // The threshold exists to keep small calls off the device; here it would make the small
        // cases measure the CPU twice and hide the very crossover being looked for.
        if (gpu is GpuComputeBackend concrete) concrete.MinimumWorkPerCall = 0;
        _gpu = gpu;

        var random = new FastRandom(1);
        _weights = new float[Width * Width];
        _biases = new float[Width];
        _input = new float[Batch * Width];
        _output = new float[Batch * Width];

        for (int i = 0; i < _weights.Length; i++) _weights[i] = random.NextGaussian() * 0.05f;
        for (int i = 0; i < _input.Length; i++) _input[i] = random.NextGaussian();
    }

    [Benchmark(Baseline = true, Description = "Dense forward (CPU SIMD)")]
    public void ForwardCpu() =>
        _cpu.DenseForward(_weights, _biases, _input, _output, Batch, Width, Width, Activation.ReLU);

    [Benchmark(Description = "Dense forward (GPU)")]
    public void ForwardGpu() =>
        _gpu.DenseForward(_weights, _biases, _input, _output, Batch, Width, Width, Activation.ReLU);

    public void Dispose()
    {
        _gpu?.Dispose();
        GC.SuppressFinalize(this);
    }
}
