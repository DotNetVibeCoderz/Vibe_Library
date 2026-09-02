// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Utils;

namespace RLNet.Neural;

/// <summary>
/// A multilayer perceptron: the function approximator behind every neural agent in RLNet.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a general computation graph. RL value and policy heads are stacks of dense
/// layers, and a fixed stack can pre-allocate every buffer it will ever need at construction —
/// which is why a full forward-backward-update cycle here allocates nothing at all.
/// </para>
/// <para>
/// <b>Why no PyTorch.</b> The obvious alternative is TorchSharp. It would bring autograd, CUDA
/// and convolutions, and it would also bring a large native dependency per platform, which
/// defeats the point of a library meant to be one portable NuGet package that runs the same on
/// Windows, Linux, macOS and ARM. For the network sizes classic control needs — two or three
/// hidden layers of 64 to 256 units — a SIMD CPU implementation is not the compromise it sounds
/// like; the GPU round-trip alone would cost more than the whole forward pass. See
/// <c>docs/en/05-neural-network.md</c> for the measured numbers behind that claim.
/// </para>
/// </remarks>
public sealed class MlpNetwork
{
    private readonly DenseLayer[] _layers;
    private readonly float[] _input;         // [maxBatch * InputSize], network-owned
    private readonly float[][] _gradBuffers; // one per layer, [maxBatch * layer.OutputSize]
    private readonly float[] _gradInputSink; // gradient w.r.t. the network input; usually discarded

    /// <summary>Largest batch this network can process, fixed at construction.</summary>
    public int MaxBatch { get; }

    /// <summary>Width of the input vector.</summary>
    public int InputSize { get; }

    /// <summary>Width of the output vector.</summary>
    public int OutputSize => _layers[^1].OutputSize;

    /// <summary>Total trainable scalars.</summary>
    public int ParameterCount { get; }

    /// <summary>The layers, in order.</summary>
    public IReadOnlyList<DenseLayer> Layers => _layers;

    /// <summary>Device this network's arithmetic runs on.</summary>
    public IComputeBackend Backend { get; } = CpuComputeBackend.Instance;

    /// <summary>Builds a network from a layer description.</summary>
    /// <param name="inputSize">Width of the input vector.</param>
    /// <param name="hiddenSizes">Widths of the hidden layers.</param>
    /// <param name="outputSize">Width of the output vector.</param>
    /// <param name="hidden">Nonlinearity on the hidden layers.</param>
    /// <param name="output">Nonlinearity on the output head.</param>
    /// <param name="maxBatch">Largest batch that will ever be pushed through.</param>
    /// <param name="random">Generator for weight initialisation.</param>
    /// <param name="outputScale">
    /// Shrinks the output head's initial weights. Policy heads use a small value so the initial
    /// distribution is near-uniform; value heads leave it at 1.
    /// </param>
    /// <param name="backend">
    /// Device the arithmetic runs on. Defaults to <see cref="CpuComputeBackend"/>; pass a GPU
    /// backend to accelerate wide networks.
    /// </param>
    public MlpNetwork(
        int inputSize,
        ReadOnlySpan<int> hiddenSizes,
        int outputSize,
        Activation hidden,
        Activation output,
        int maxBatch,
        FastRandom random,
        float outputScale = 1f,
        IComputeBackend? backend = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBatch, 1);

        InputSize = inputSize;
        MaxBatch = maxBatch;
        Backend = backend ?? CpuComputeBackend.Instance;

        _layers = new DenseLayer[hiddenSizes.Length + 1];
        int previous = inputSize;
        for (int i = 0; i < hiddenSizes.Length; i++)
        {
            _layers[i] = new DenseLayer(previous, hiddenSizes[i], hidden, maxBatch, random, 1f, Backend);
            previous = hiddenSizes[i];
        }
        _layers[^1] = new DenseLayer(previous, outputSize, output, maxBatch, random, outputScale, Backend);

        _input = new float[maxBatch * inputSize];
        _gradBuffers = new float[_layers.Length][];
        for (int i = 0; i < _layers.Length; i++)
            _gradBuffers[i] = new float[maxBatch * _layers[i].OutputSize];
        _gradInputSink = new float[maxBatch * inputSize];

        ParameterCount = _layers.Sum(l => l.ParameterCount);
    }

    /// <summary>Scratch for the caller to fill with the batch before calling <see cref="Forward(int)"/>.</summary>
    /// <remarks>
    /// Handing out the buffer rather than accepting one lets callers build a minibatch in place —
    /// a replay sample writes straight here instead of into a temporary that is then copied.
    /// </remarks>
    public Span<float> InputBuffer(int batch) => _input.AsSpan(0, batch * InputSize);

    /// <summary>
    /// Runs the network over the batch currently in <see cref="InputBuffer"/> and returns the
    /// result, valid until the next forward pass.
    /// </summary>
    public ReadOnlySpan<float> Forward(int batch)
    {
        if (batch > MaxBatch)
            throw new ArgumentOutOfRangeException(nameof(batch), $"Batch {batch} exceeds MaxBatch {MaxBatch}.");

        float[] current = _input;
        for (int i = 0; i < _layers.Length; i++)
            current = _layers[i].Forward(current, batch);

        return current.AsSpan(0, batch * OutputSize);
    }

    /// <summary>Convenience single-sample forward pass.</summary>
    public ReadOnlySpan<float> Forward(ReadOnlySpan<float> input)
    {
        input.CopyTo(InputBuffer(1));
        return Forward(1);
    }

    /// <summary>Scratch for the loss gradient with respect to the network output.</summary>
    public Span<float> OutputGradientBuffer(int batch) => _gradBuffers[^1].AsSpan(0, batch * OutputSize);

    /// <summary>
    /// Backpropagates the gradient in <see cref="OutputGradientBuffer"/>, accumulating parameter
    /// gradients across the network.
    /// </summary>
    /// <param name="batch">Batch size, which must match the preceding forward pass.</param>
    /// <param name="gradInput">
    /// Receives the gradient with respect to the network input. Only non-empty when one network's
    /// loss flows into another's output — SAC's actor differentiating through its critic is the
    /// case that needs it.
    /// </param>
    public void Backward(int batch, Span<float> gradInput = default)
    {
        for (int i = _layers.Length - 1; i >= 0; i--)
        {
            var gradOut = _gradBuffers[i].AsSpan(0, batch * _layers[i].OutputSize);

            Span<float> gradIn = i == 0
                ? (gradInput.IsEmpty ? Span<float>.Empty : gradInput)
                : _gradBuffers[i - 1].AsSpan(0, batch * _layers[i - 1].OutputSize);

            _layers[i].Backward(gradOut, gradIn);
        }
    }

    /// <summary>
    /// Backpropagates and also reports the gradient with respect to the network input, in the
    /// network's own sink buffer.
    /// </summary>
    public ReadOnlySpan<float> BackwardToInput(int batch)
    {
        Backward(batch, _gradInputSink.AsSpan(0, batch * InputSize));
        return _gradInputSink.AsSpan(0, batch * InputSize);
    }

    /// <summary>Zeroes every accumulated parameter gradient.</summary>
    public void ZeroGradients()
    {
        foreach (var layer in _layers) layer.ZeroGradients();
    }

    /// <summary>Applies one optimiser step to every layer, honouring the optimiser's gradient-norm clip.</summary>
    /// <param name="optimizer">The optimiser holding this network's moment estimates.</param>
    /// <param name="gradientScale">Usually the reciprocal of the batch size, to decouple step size from batch size.</param>
    public void ApplyGradients(AdamOptimizer optimizer, float gradientScale = 1f)
    {
        float scale = gradientScale;

        if (optimizer.MaxGradientNorm > 0f)
        {
            // The clip is global across the network, not per layer: the quantity that destroys a
            // policy is the norm of the whole step, and clipping layer by layer would leave a
            // network whose total step is still far past the limit.
            float sumSquares = 0f;
            foreach (var layer in _layers)
            {
                sumSquares += SimdOps.SumSquares(layer.WeightGradients);
                sumSquares += SimdOps.SumSquares(layer.BiasGradients);
            }

            float norm = MathF.Sqrt(sumSquares) * MathF.Abs(gradientScale);
            if (norm > optimizer.MaxGradientNorm)
                scale *= optimizer.MaxGradientNorm / norm;
        }

        var scope = optimizer.BeginUpdate(scale);
        foreach (var layer in _layers)
        {
            scope.Apply(layer.Weights, layer.WeightGradients);
            scope.Apply(layer.Biases, layer.BiasGradients);
        }
    }

    /// <summary>Copies every parameter into a new flat array.</summary>
    public float[] ExportParameters()
    {
        var buffer = new float[ParameterCount];
        ExportParameters(buffer);
        return buffer;
    }

    /// <summary>Copies every parameter into <paramref name="destination"/>.</summary>
    public void ExportParameters(Span<float> destination)
    {
        int offset = 0;
        foreach (var layer in _layers)
            offset += layer.ExportParameters(destination[offset..]);
    }

    /// <summary>Loads every parameter from <paramref name="source"/>.</summary>
    public void ImportParameters(ReadOnlySpan<float> source)
    {
        if (source.Length != ParameterCount)
            throw new ArgumentException($"Expected {ParameterCount} parameters, got {source.Length}.", nameof(source));

        int offset = 0;
        foreach (var layer in _layers)
            offset += layer.ImportParameters(source[offset..]);
    }

    /// <summary>Copies another network's parameters over this one's — a hard target-network sync.</summary>
    public void CopyFrom(MlpNetwork source)
    {
        for (int i = 0; i < _layers.Length; i++)
        {
            source._layers[i].Weights.CopyTo(_layers[i].Weights);
            source._layers[i].Biases.CopyTo(_layers[i].Biases);
        }
    }

    /// <summary>
    /// Moves this network a fraction <paramref name="tau"/> of the way toward
    /// <paramref name="source"/> — the Polyak soft update SAC and TD3 use for their targets.
    /// </summary>
    public void SoftUpdateFrom(MlpNetwork source, float tau)
    {
        for (int i = 0; i < _layers.Length; i++)
        {
            SimdOps.PolyakBlend(_layers[i].Weights, source._layers[i].Weights, tau);
            SimdOps.PolyakBlend(_layers[i].Biases, source._layers[i].Biases, tau);
        }
    }
}
