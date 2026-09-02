// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;
using RLNet.Neural;

namespace RLNet.Gpu;

/// <summary>
/// Runs the dense-layer kernels on a GPU through ILGPU, falling back to the CPU when no
/// accelerator is available.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read this before reaching for it.</b> GPU is not automatically faster here, and for most of
/// what RLNet does it is slower. Classic-control networks are two or three layers of 64 to 256
/// units; a forward pass over a batch of 256 is a few hundred microseconds of arithmetic, while
/// the round trip to move the batch across the bus and bring the result back costs tens of
/// microseconds on its own and does not shrink as the network does. The crossover is roughly a
/// hidden width of 512 with a batch of 256 — below that, <see cref="CpuComputeBackend"/> wins.
/// Measure on the actual configuration before switching.
/// </para>
/// <para>
/// Where it does pay off: wide networks over image-like observations, large-batch offline
/// updates, and sweeps that train many agents at once. That is the shape this exists for.
/// </para>
/// <para>
/// <see cref="MinimumWorkPerCall"/> makes the trade automatically. Any call smaller than the
/// threshold runs on the CPU path rather than paying for a transfer, which matters because a
/// single-observation forward pass happens on every environment step and would otherwise be
/// dominated entirely by launch overhead.
/// </para>
/// </remarks>
public sealed class GpuComputeBackend : IComputeBackend
{
    private readonly Context _context;
    private readonly Accelerator _accelerator;
    private readonly CpuComputeBackend _fallback = CpuComputeBackend.Instance;

    private readonly Action<Index2D, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>, int, int, int> _forwardKernel;
    private readonly Action<Index2D, ArrayView<float>, ArrayView<float>, ArrayView<float>, int, int, int> _gradInputKernel;
    private readonly Action<Index2D, ArrayView<float>, ArrayView<float>, ArrayView<float>, int, int, int> _gradWeightKernel;
    private readonly Action<Index1D, ArrayView<float>, ArrayView<float>, int, int> _gradBiasKernel;
    private readonly Action<Index1D, ArrayView<float>, ArrayView<float>, int> _activationBackwardKernel;

    // Device buffers are cached and grown rather than allocated per call: an allocation on an
    // accelerator is a synchronising operation, and doing one per gradient step would cost more
    // than the kernels save.
    private MemoryBuffer1D<float, Stride1D.Dense>? _weightBuffer;
    private MemoryBuffer1D<float, Stride1D.Dense>? _biasBuffer;
    private MemoryBuffer1D<float, Stride1D.Dense>? _inputBuffer;
    private MemoryBuffer1D<float, Stride1D.Dense>? _outputBuffer;
    private MemoryBuffer1D<float, Stride1D.Dense>? _gradOutputBuffer;
    private MemoryBuffer1D<float, Stride1D.Dense>? _gradInputBuffer;
    private MemoryBuffer1D<float, Stride1D.Dense>? _gradWeightBuffer;
    private MemoryBuffer1D<float, Stride1D.Dense>? _gradBiasBuffer;

    private bool _disposed;

    /// <summary>
    /// Element count below which a call runs on the CPU instead of the accelerator.
    /// </summary>
    /// <remarks>
    /// Measured as <c>batch × inputSize × outputSize</c>, the multiply-accumulate count of the
    /// layer. The default is deliberately high: below it the transfer dominates so completely
    /// that using the GPU is a slowdown, not a smaller speedup.
    /// </remarks>
    public long MinimumWorkPerCall { get; set; } = 1 << 20;

    /// <summary>Creates a backend on the best available accelerator.</summary>
    /// <param name="preferCuda">Prefer CUDA over OpenCL when both are present.</param>
    /// <exception cref="NotSupportedException">No GPU accelerator was found.</exception>
    public GpuComputeBackend(bool preferCuda = true)
    {
        _context = Context.Create(builder => builder.Default().EnableAlgorithms());

        var device = SelectDevice(_context, preferCuda)
            ?? throw new NotSupportedException(
                "No CUDA or OpenCL accelerator was found. Use CpuComputeBackend, or " +
                "GpuComputeBackend.TryCreate to fall back automatically.");

        _accelerator = device.CreateAccelerator(_context);

        _forwardKernel = _accelerator.LoadAutoGroupedStreamKernel<
            Index2D, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>, int, int, int>(ForwardKernel);
        _gradInputKernel = _accelerator.LoadAutoGroupedStreamKernel<
            Index2D, ArrayView<float>, ArrayView<float>, ArrayView<float>, int, int, int>(GradInputKernel);
        _gradWeightKernel = _accelerator.LoadAutoGroupedStreamKernel<
            Index2D, ArrayView<float>, ArrayView<float>, ArrayView<float>, int, int, int>(GradWeightKernel);
        _gradBiasKernel = _accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<float>, ArrayView<float>, int, int>(GradBiasKernel);
        _activationBackwardKernel = _accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<float>, ArrayView<float>, int>(ActivationBackwardKernel);
    }

    /// <summary>
    /// Returns a GPU backend, or the CPU backend when no accelerator is available.
    /// </summary>
    /// <remarks>
    /// The form to use in application code. A machine without a GPU is the common case, and it
    /// should mean "run on the CPU", not "crash on startup".
    /// </remarks>
    public static IComputeBackend TryCreate(bool preferCuda = true)
    {
        try
        {
            return new GpuComputeBackend(preferCuda);
        }
        catch (Exception exception) when (exception is NotSupportedException or TypeInitializationException or DllNotFoundException)
        {
            return CpuComputeBackend.Instance;
        }
    }

    /// <summary>Lists the accelerators ILGPU can see, for diagnostics.</summary>
    public static IReadOnlyList<string> AvailableDevices()
    {
        using var context = Context.Create(builder => builder.Default());
        return [.. context.Devices
            .Where(d => d.AcceleratorType != AcceleratorType.CPU)
            .Select(d => $"{d.AcceleratorType}: {d.Name}")];
    }

    private static Device? SelectDevice(Context context, bool preferCuda)
    {
        var cuda = context.GetPreferredDevices(preferCPU: false, matchingDevicesOnly: false)
            .OfType<CudaDevice>().FirstOrDefault();
        var openCl = context.GetPreferredDevices(preferCPU: false, matchingDevicesOnly: false)
            .OfType<CLDevice>().FirstOrDefault();

        return preferCuda ? cuda ?? (Device?)openCl : openCl ?? (Device?)cuda;
    }

    public string Name => $"GPU ({_accelerator.AcceleratorType}: {_accelerator.Name})";

    public bool IsAccelerated => true;

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
        if ((long)batch * inputSize * outputSize < MinimumWorkPerCall)
        {
            _fallback.DenseForward(weights, biases, input, output, batch, inputSize, outputSize, activation);
            return;
        }

        var weightView = Upload(ref _weightBuffer, weights);
        var biasView = Upload(ref _biasBuffer, biases);
        var inputView = Upload(ref _inputBuffer, input);
        var outputView = Ensure(ref _outputBuffer, output.Length);

        _forwardKernel(
            new Index2D(batch, outputSize),
            weightView, biasView, inputView, outputView,
            inputSize, outputSize, (int)activation);

        _accelerator.Synchronize();
        outputView.SubView(0, output.Length).CopyToCPU(output);
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
        if ((long)batch * inputSize * outputSize < MinimumWorkPerCall)
        {
            _fallback.DenseBackward(
                weights, input, output, gradOutput, gradInput, weightGrad, biasGrad,
                batch, inputSize, outputSize, activation);
            return;
        }

        // The nonlinearity's derivative is folded in first, on the device, so gradOutput comes
        // back already multiplied through and the three products below are pure linear algebra —
        // the same decomposition the CPU backend uses.
        var outputView = Upload(ref _outputBuffer, output);
        var gradOutputView = Upload(ref _gradOutputBuffer, gradOutput);

        _activationBackwardKernel(gradOutput.Length, outputView, gradOutputView, (int)activation);

        var weightView = Upload(ref _weightBuffer, weights);
        var inputView = Upload(ref _inputBuffer, input);

        // Gradients accumulate across calls, so the existing contents have to go up with them
        // rather than starting from a zeroed device buffer.
        var gradWeightView = Upload(ref _gradWeightBuffer, weightGrad);
        var gradBiasView = Upload(ref _gradBiasBuffer, biasGrad);

        _gradWeightKernel(
            new Index2D(inputSize, outputSize),
            inputView, gradOutputView, gradWeightView, batch, inputSize, outputSize);

        _gradBiasKernel(outputSize, gradOutputView, gradBiasView, batch, outputSize);

        if (!gradInput.IsEmpty)
        {
            var gradInputView = Ensure(ref _gradInputBuffer, gradInput.Length);
            _gradInputKernel(
                new Index2D(batch, inputSize),
                weightView, gradOutputView, gradInputView, inputSize, outputSize, batch);

            _accelerator.Synchronize();
            gradInputView.SubView(0, gradInput.Length).CopyToCPU(gradInput);
        }

        _accelerator.Synchronize();
        gradOutputView.SubView(0, gradOutput.Length).CopyToCPU(gradOutput);
        gradWeightView.SubView(0, weightGrad.Length).CopyToCPU(weightGrad);
        gradBiasView.SubView(0, biasGrad.Length).CopyToCPU(biasGrad);
    }

    // --- Kernels -----------------------------------------------------------------------------
    // One thread per output element. ILGPU compiles these to PTX or OpenCL C at load time, so
    // they must stay inside the subset it supports: no allocations, no exceptions, no closures.

    private static void ForwardKernel(
        Index2D index,
        ArrayView<float> weights,
        ArrayView<float> biases,
        ArrayView<float> input,
        ArrayView<float> output,
        int inputSize,
        int outputSize,
        int activation)
    {
        int sample = index.X;
        int unit = index.Y;

        float sum = biases[unit];
        for (int i = 0; i < inputSize; i++)
            sum += input[sample * inputSize + i] * weights[i * outputSize + unit];

        output[sample * outputSize + unit] = ApplyActivation(sum, activation);
    }

    private static void GradInputKernel(
        Index2D index,
        ArrayView<float> weights,
        ArrayView<float> gradOutput,
        ArrayView<float> gradInput,
        int inputSize,
        int outputSize,
        int batch)
    {
        int sample = index.X;
        int feature = index.Y;

        float sum = 0f;
        for (int unit = 0; unit < outputSize; unit++)
            sum += gradOutput[sample * outputSize + unit] * weights[feature * outputSize + unit];

        gradInput[sample * inputSize + feature] = sum;
    }

    private static void GradWeightKernel(
        Index2D index,
        ArrayView<float> input,
        ArrayView<float> gradOutput,
        ArrayView<float> gradWeight,
        int batch,
        int inputSize,
        int outputSize)
    {
        int feature = index.X;
        int unit = index.Y;

        // Each thread owns one weight and sums over the batch itself, so no two threads ever
        // write the same element and the kernel needs no atomics.
        float sum = 0f;
        for (int sample = 0; sample < batch; sample++)
            sum += input[sample * inputSize + feature] * gradOutput[sample * outputSize + unit];

        gradWeight[feature * outputSize + unit] += sum;
    }

    private static void GradBiasKernel(
        Index1D index,
        ArrayView<float> gradOutput,
        ArrayView<float> gradBias,
        int batch,
        int outputSize)
    {
        float sum = 0f;
        for (int sample = 0; sample < batch; sample++)
            sum += gradOutput[sample * outputSize + index];

        gradBias[index] += sum;
    }

    private static void ActivationBackwardKernel(
        Index1D index,
        ArrayView<float> output,
        ArrayView<float> gradOutput,
        int activation)
    {
        float y = output[index];

        // Mirrors RLNet.Neural.Activations.Backward. Kept as an integer switch because the
        // kernel compiler cannot see the enum.
        if (activation == 1) // ReLU
        {
            if (y <= 0f) gradOutput[index] = 0f;
        }
        else if (activation == 2) // Tanh
        {
            gradOutput[index] *= 1f - y * y;
        }
    }

    private static float ApplyActivation(float value, int activation) => activation switch
    {
        1 => value < 0f ? 0f : value,
        2 => XMath.Tanh(value),
        _ => value,
    };

    // --- Buffer management -------------------------------------------------------------------

    private ArrayView<float> Upload(ref MemoryBuffer1D<float, Stride1D.Dense>? buffer, ReadOnlySpan<float> data)
    {
        var view = Ensure(ref buffer, data.Length);
        view.SubView(0, data.Length).CopyFromCPU(data);
        return view;
    }

    private ArrayView<float> Ensure(ref MemoryBuffer1D<float, Stride1D.Dense>? buffer, int length)
    {
        // Buffers only ever grow. Shapes repeat across a training run, so after the first few
        // calls this stops allocating entirely.
        if (buffer is null || buffer.Length < length)
        {
            buffer?.Dispose();
            buffer = _accelerator.Allocate1D<float>(length);
        }
        return buffer.View;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _weightBuffer?.Dispose();
        _biasBuffer?.Dispose();
        _inputBuffer?.Dispose();
        _outputBuffer?.Dispose();
        _gradOutputBuffer?.Dispose();
        _gradInputBuffer?.Dispose();
        _gradWeightBuffer?.Dispose();
        _gradBiasBuffer?.Dispose();

        _accelerator.Dispose();
        _context.Dispose();
    }
}
