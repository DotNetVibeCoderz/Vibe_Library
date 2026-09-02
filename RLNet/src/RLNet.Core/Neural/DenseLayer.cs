// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Utils;

namespace RLNet.Neural;

/// <summary>A fully connected layer with a bias and an elementwise nonlinearity.</summary>
/// <remarks>
/// <para>
/// The layer owns its parameters, gradients and activation cache, all sized once for the largest
/// batch it will ever see. Nothing in <see cref="Forward"/> or <see cref="Backward"/> allocates;
/// the arithmetic itself is delegated to an <see cref="IComputeBackend"/>, which is what lets the
/// same layer run on CPU SIMD or on a GPU.
/// </para>
/// <para>
/// Weights are stored row-major as <c>[inputSize, outputSize]</c>. That layout is a contract with
/// the backends, not an implementation detail: it is what makes all three passes walk memory
/// contiguously along the output dimension.
/// </para>
/// </remarks>
public sealed class DenseLayer
{
    private readonly IComputeBackend _backend;

    private readonly float[] _weights;      // [inputSize * outputSize], row-major by input
    private readonly float[] _biases;       // [outputSize]
    private readonly float[] _weightGrad;
    private readonly float[] _biasGrad;

    private readonly float[] _output;       // [maxBatch * outputSize], cached post-activation
    private float[] _lastInput = [];        // borrowed reference to the caller's input batch
    private int _lastBatch;

    public int InputSize { get; }
    public int OutputSize { get; }
    public Activation Activation { get; }

    /// <summary>Number of trainable scalars in this layer.</summary>
    public int ParameterCount => _weights.Length + _biases.Length;

    internal Span<float> Weights => _weights;
    internal Span<float> Biases => _biases;
    internal Span<float> WeightGradients => _weightGrad;
    internal Span<float> BiasGradients => _biasGrad;

    public DenseLayer(
        int inputSize,
        int outputSize,
        Activation activation,
        int maxBatch,
        FastRandom random,
        float outputScale = 1f,
        IComputeBackend? backend = null)
    {
        InputSize = inputSize;
        OutputSize = outputSize;
        Activation = activation;
        _backend = backend ?? CpuComputeBackend.Instance;

        _weights = new float[inputSize * outputSize];
        _biases = new float[outputSize];
        _weightGrad = new float[_weights.Length];
        _biasGrad = new float[_biases.Length];
        _output = new float[maxBatch * outputSize];

        InitialiseWeights(random, outputScale);
    }

    private void InitialiseWeights(FastRandom random, float outputScale)
    {
        // He initialisation for ReLU, Xavier otherwise: the variance that keeps the forward
        // signal from collapsing or exploding through a stack of layers. outputScale then shrinks
        // the final head — a policy head initialised near zero starts as a near-uniform
        // distribution, which is the difference between PPO exploring and PPO committing to one
        // action on step one and never recovering.
        float gain = Activation == Activation.ReLU ? 2f : 1f;
        float std = MathF.Sqrt(gain / InputSize) * outputScale;

        for (int i = 0; i < _weights.Length; i++)
            _weights[i] = random.NextGaussian() * std;
        // Biases stay at zero: symmetry is already broken by the weights.
    }

    /// <summary>
    /// Runs a batch forward. <paramref name="input"/> holds <c>[batch, InputSize]</c> values; the
    /// result is layer-owned <c>[batch, OutputSize]</c> memory, valid until the next call.
    /// </summary>
    /// <remarks>
    /// The input arrives as an array rather than a span because the layer keeps a reference to it
    /// for <see cref="Backward"/>, and a span cannot be stored in a field. <see cref="MlpNetwork"/>
    /// owns every buffer in the chain, so the reference is always to memory that outlives the pass.
    /// </remarks>
    public float[] Forward(float[] input, int batch)
    {
        _lastInput = input;
        _lastBatch = batch;

        _backend.DenseForward(
            _weights,
            _biases,
            input.AsSpan(0, batch * InputSize),
            _output.AsSpan(0, batch * OutputSize),
            batch,
            InputSize,
            OutputSize,
            Activation);

        return _output;
    }

    /// <summary>
    /// Backpropagates <paramref name="gradOutput"/> (<c>[batch, OutputSize]</c>), accumulating
    /// weight and bias gradients and writing the gradient with respect to this layer's input into
    /// <paramref name="gradInput"/>. Pass an empty span for the first layer of a network.
    /// </summary>
    public void Backward(Span<float> gradOutput, Span<float> gradInput)
    {
        int batch = _lastBatch;

        _backend.DenseBackward(
            _weights,
            _lastInput.AsSpan(0, batch * InputSize),
            _output.AsSpan(0, batch * OutputSize),
            gradOutput,
            gradInput,
            _weightGrad,
            _biasGrad,
            batch,
            InputSize,
            OutputSize,
            Activation);
    }

    /// <summary>Zeroes accumulated gradients.</summary>
    public void ZeroGradients()
    {
        Array.Clear(_weightGrad);
        Array.Clear(_biasGrad);
    }

    /// <summary>Copies this layer's parameters into <paramref name="destination"/> and returns how many were written.</summary>
    public int ExportParameters(Span<float> destination)
    {
        _weights.CopyTo(destination);
        _biases.CopyTo(destination[_weights.Length..]);
        return ParameterCount;
    }

    /// <summary>Loads this layer's parameters from <paramref name="source"/> and returns how many were read.</summary>
    public int ImportParameters(ReadOnlySpan<float> source)
    {
        source[.._weights.Length].CopyTo(_weights);
        source.Slice(_weights.Length, _biases.Length).CopyTo(_biases);
        return ParameterCount;
    }
}
