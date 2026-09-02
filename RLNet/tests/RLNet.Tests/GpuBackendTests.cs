// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Gpu;
using RLNet.Neural;
using RLNet.Utils;
using Xunit;

namespace RLNet.Tests;

/// <summary>
/// Checks that the GPU backend computes the same thing the CPU backend does.
/// </summary>
/// <remarks>
/// <para>
/// A second implementation of the same arithmetic is a second place for it to be wrong, and a
/// GPU kernel that is subtly wrong produces an agent that trains slightly worse — which is
/// invisible without a comparison. Every test here runs the identical input through both
/// backends and requires agreement.
/// </para>
/// <para>
/// They skip themselves when no accelerator is present, which is the normal case on a CI runner
/// and on most developer machines. A skipped test is honest; a test that silently passes by
/// running the CPU path twice is not, so each one asserts it really got an accelerator first.
/// </para>
/// </remarks>
[Trait("Category", "Gpu")]
public class GpuBackendTests
{
    private static IComputeBackend? TryGetAccelerator()
    {
        var backend = GpuComputeBackend.TryCreate();
        return backend.IsAccelerated ? backend : null;
    }

    [Fact]
    public void DenseForward_MatchesTheCpuBackend()
    {
        using var gpu = TryGetAccelerator();
        Assert.SkipUnless(gpu is not null, "No CUDA or OpenCL accelerator on this machine.");

        // Above GpuComputeBackend.MinimumWorkPerCall, so the call really reaches the device
        // rather than quietly falling back to the CPU path being compared against.
        const int batch = 256, inputSize = 128, outputSize = 128;

        var random = new FastRandom(1);
        var weights = Fill(inputSize * outputSize, random);
        var biases = Fill(outputSize, random);
        var input = Fill(batch * inputSize, random);

        foreach (var activation in new[] { Activation.Linear, Activation.ReLU, Activation.Tanh })
        {
            var cpuOutput = new float[batch * outputSize];
            var gpuOutput = new float[batch * outputSize];

            CpuComputeBackend.Instance.DenseForward(
                weights, biases, input, cpuOutput, batch, inputSize, outputSize, activation);
            gpu!.DenseForward(
                weights, biases, input, gpuOutput, batch, inputSize, outputSize, activation);

            AssertClose(cpuOutput, gpuOutput, $"forward/{activation}");
        }
    }

    [Fact]
    public void DenseBackward_MatchesTheCpuBackend()
    {
        using var gpu = TryGetAccelerator();
        Assert.SkipUnless(gpu is not null, "No CUDA or OpenCL accelerator on this machine.");

        const int batch = 256, inputSize = 128, outputSize = 128;

        var random = new FastRandom(2);
        var weights = Fill(inputSize * outputSize, random);
        var input = Fill(batch * inputSize, random);
        var output = Fill(batch * outputSize, random);
        var gradOutput = Fill(batch * outputSize, random);

        foreach (var activation in new[] { Activation.Linear, Activation.ReLU, Activation.Tanh })
        {
            var cpuGradOutput = (float[])gradOutput.Clone();
            var cpuGradInput = new float[batch * inputSize];
            var cpuWeightGrad = new float[inputSize * outputSize];
            var cpuBiasGrad = new float[outputSize];

            var gpuGradOutput = (float[])gradOutput.Clone();
            var gpuGradInput = new float[batch * inputSize];
            var gpuWeightGrad = new float[inputSize * outputSize];
            var gpuBiasGrad = new float[outputSize];

            CpuComputeBackend.Instance.DenseBackward(
                weights, input, output, cpuGradOutput, cpuGradInput, cpuWeightGrad, cpuBiasGrad,
                batch, inputSize, outputSize, activation);

            gpu!.DenseBackward(
                weights, input, output, gpuGradOutput, gpuGradInput, gpuWeightGrad, gpuBiasGrad,
                batch, inputSize, outputSize, activation);

            AssertClose(cpuGradInput, gpuGradInput, $"gradInput/{activation}");
            AssertClose(cpuWeightGrad, gpuWeightGrad, $"weightGrad/{activation}");
            AssertClose(cpuBiasGrad, gpuBiasGrad, $"biasGrad/{activation}");
        }
    }

    [Fact]
    public void SmallCallsFallBackToTheCpu()
    {
        // A single-observation forward pass happens on every environment step. Sending it to the
        // device would make action selection slower than the whole rest of the loop, so the
        // threshold has to route it back to the CPU.
        using var gpu = TryGetAccelerator();
        Assert.SkipUnless(gpu is not null, "No CUDA or OpenCL accelerator on this machine.");

        var random = new FastRandom(3);
        var weights = Fill(4 * 8, random);
        var biases = Fill(8, random);
        var input = Fill(4, random);

        var expected = new float[8];
        var actual = new float[8];

        CpuComputeBackend.Instance.DenseForward(weights, biases, input, expected, 1, 4, 8, Activation.ReLU);
        gpu!.DenseForward(weights, biases, input, actual, 1, 4, 8, Activation.ReLU);

        // Identical to the last bit, because it is literally the same code path.
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TryCreateNeverThrows()
    {
        // The contract that makes the GPU package safe to reference unconditionally: on a machine
        // with no accelerator it returns the CPU backend rather than failing.
        using var backend = GpuComputeBackend.TryCreate();
        Assert.NotNull(backend);
        Assert.False(string.IsNullOrWhiteSpace(backend.Name));
    }

    private static float[] Fill(int length, FastRandom random)
    {
        var values = new float[length];
        for (int i = 0; i < length; i++) values[i] = random.NextGaussian() * 0.1f;
        return values;
    }

    private static void AssertClose(float[] expected, float[] actual, string what)
    {
        // Float summation order differs between a serial CPU loop and a parallel kernel, so exact
        // equality is not achievable. The tolerance is tight enough that a real kernel bug -
        // a transposed index, a missing term - is orders of magnitude outside it.
        for (int i = 0; i < expected.Length; i++)
        {
            float tolerance = 1e-3f * Math.Max(1f, Math.Abs(expected[i]));
            Assert.True(
                Math.Abs(expected[i] - actual[i]) <= tolerance,
                $"{what}[{i}]: CPU {expected[i]:F6}, GPU {actual[i]:F6}");
        }
    }
}
