// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace RLNet.Neural;

/// <summary>
/// The device a network's linear algebra runs on.
/// </summary>
/// <remarks>
/// <para>
/// A dense layer is three matrix products — forward, gradient with respect to input, gradient
/// with respect to weights — and essentially all of a training run's arithmetic is inside them.
/// Routing exactly those three through an interface is what lets the same
/// <see cref="MlpNetwork"/> run on CPU SIMD or on a GPU without either the layers or the agents
/// knowing which.
/// </para>
/// <para>
/// The nonlinearity is part of the contract rather than applied afterwards by the caller,
/// because a device backend wants to fuse it into the same kernel; making it a separate step
/// would force a round trip per layer and give back most of what the accelerator won.
/// </para>
/// <para>
/// <b>CPU is the default and is usually the right answer.</b> Classic-control networks are two
/// or three layers of 64 to 256 units, and at that size the PCIe round trip costs more than the
/// arithmetic saves. GPU pays off on wide networks and large batches — see
/// <c>docs/en/05-neural-network.md</c> for the measured crossover.
/// </para>
/// </remarks>
public interface IComputeBackend : IDisposable
{
    /// <summary>Human-readable device name, shown in the visualizer and benchmark output.</summary>
    string Name { get; }

    /// <summary>Whether this backend runs on a hardware accelerator rather than the CPU.</summary>
    bool IsAccelerated { get; }

    /// <summary>
    /// Computes <c>output = activation(input · weights + biases)</c> for a whole batch.
    /// </summary>
    /// <param name="weights">Row-major <c>[inputSize, outputSize]</c>.</param>
    /// <param name="biases"><c>[outputSize]</c>.</param>
    /// <param name="input">Row-major <c>[batch, inputSize]</c>.</param>
    /// <param name="output">Row-major <c>[batch, outputSize]</c>, overwritten.</param>
    void DenseForward(
        ReadOnlySpan<float> weights,
        ReadOnlySpan<float> biases,
        ReadOnlySpan<float> input,
        Span<float> output,
        int batch,
        int inputSize,
        int outputSize,
        Activation activation);

    /// <summary>
    /// Backpropagates one dense layer: folds the nonlinearity into
    /// <paramref name="gradOutput"/>, accumulates into <paramref name="weightGrad"/> and
    /// <paramref name="biasGrad"/>, and writes the gradient with respect to the layer input.
    /// </summary>
    /// <param name="output">The cached post-activation output of the matching forward pass.</param>
    /// <param name="gradOutput">Incoming gradient, modified in place by the nonlinearity's derivative.</param>
    /// <param name="gradInput">
    /// Receives the gradient with respect to the input. Pass an empty span for the first layer
    /// of a network, which lets the backend skip that product entirely.
    /// </param>
    void DenseBackward(
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
        Activation activation);
}
