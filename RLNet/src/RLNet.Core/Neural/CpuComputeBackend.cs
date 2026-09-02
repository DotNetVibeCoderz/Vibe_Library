// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Utils;

namespace RLNet.Neural;

/// <summary>The default backend: vectorised single-threaded CPU arithmetic.</summary>
/// <remarks>
/// <para>
/// Single-threaded on purpose. A network small enough for classic control finishes a forward
/// pass in a few microseconds, and the cost of waking thread-pool workers is larger than the
/// work handed to them. Parallelism in RL belongs one level up — at the environment, where
/// <c>VectorEnvironment</c> steps many copies at once — not inside a 64-wide layer.
/// </para>
/// <para>
/// The weight layout does the heavy lifting here. Weights are row-major <c>[inputSize,
/// outputSize]</c>, so every inner loop walks the output dimension contiguously and reduces to
/// one <see cref="SimdOps"/> call, on all three passes.
/// </para>
/// </remarks>
public sealed class CpuComputeBackend : IComputeBackend
{
    /// <summary>The shared instance. It is stateless, so one is enough for a whole process.</summary>
    public static CpuComputeBackend Instance { get; } = new();

    public string Name => $"CPU SIMD ({System.Numerics.Vector<float>.Count * 32}-bit vectors)";

    public bool IsAccelerated => false;

    public void DenseForward(
        ReadOnlySpan<float> weights,
        ReadOnlySpan<float> biases,
        ReadOnlySpan<float> input,
        Span<float> output,
        int batch,
        int inputSize,
        int outputSize,
        Activation activation)
    {
        for (int b = 0; b < batch; b++)
        {
            var row = output.Slice(b * outputSize, outputSize);
            biases.CopyTo(row);

            var sample = input.Slice(b * inputSize, inputSize);
            for (int i = 0; i < inputSize; i++)
            {
                float x = sample[i];

                // Skipping zeros is not a micro-optimisation: after a ReLU roughly half of a
                // hidden layer's activations are exactly zero, so this elides close to half the
                // work in every layer past the first.
                if (x != 0f)
                    SimdOps.AddScaled(row, weights.Slice(i * outputSize, outputSize), x);
            }

            Activations.Forward(activation, row);
        }
    }

    public void DenseBackward(
        ReadOnlySpan<float> weights,
        ReadOnlySpan<float> input,
        ReadOnlySpan<float> output,
        Span<float> gradOutput,
        Span<float> gradInput,
        Span<float> weightGrad,
        Span<float> biasGrad,
        int batch,
        int inputSize,
        int outputSize,
        Activation activation)
    {
        // Fold the nonlinearity in first, so everything below is pure linear algebra.
        Activations.Backward(activation, output, gradOutput);

        if (!gradInput.IsEmpty) gradInput.Clear();

        for (int b = 0; b < batch; b++)
        {
            var gOut = gradOutput.Slice(b * outputSize, outputSize);
            var sample = input.Slice(b * inputSize, inputSize);

            SimdOps.AddScaled(biasGrad, gOut, 1f);

            if (gradInput.IsEmpty)
            {
                for (int i = 0; i < inputSize; i++)
                {
                    float x = sample[i];
                    if (x != 0f)
                        SimdOps.AddScaled(weightGrad.Slice(i * outputSize, outputSize), gOut, x);
                }
            }
            else
            {
                var gIn = gradInput.Slice(b * inputSize, inputSize);
                for (int i = 0; i < inputSize; i++)
                {
                    // Both products need row i of W and the same output gradient, so they are
                    // fused into a single pass over that row while it is in cache.
                    gIn[i] = SimdOps.Dot(gOut, weights.Slice(i * outputSize, outputSize));

                    float x = sample[i];
                    if (x != 0f)
                        SimdOps.AddScaled(weightGrad.Slice(i * outputSize, outputSize), gOut, x);
                }
            }
        }
    }

    /// <summary>Nothing to release; the shared instance holds no unmanaged state.</summary>
    public void Dispose() { }
}
