# GPU (optional)

[← Documentation index](README.md) · [Bahasa Indonesia](../id/06-gpu.md)

```bash
dotnet add package RLNet.Gpu
```

```csharp
using RLNet.Gpu;

using var backend = GpuComputeBackend.TryCreate();
var agent = new DqnAgent(obs, act, backend: backend);
```

## Read this first

**GPU is not automatically faster here, and for most of what RLNet does it is slower.**

Classic-control networks are two or three layers of 64 to 256 units. A forward pass over a batch of
256 is a few hundred microseconds of arithmetic, while the round trip to move the batch across the
bus and bring the result back costs tens of microseconds on its own — and does not shrink as the
network does. Below roughly a hidden width of 512 with a batch of 256,
[`CpuComputeBackend`](05-neural-network.md) wins outright.

Where it does pay off:

- Wide networks — hidden layers of 512 or more
- Large-batch offline updates
- Image-like observations, if you extend the library that far
- Sweeps training many agents at once

**Measure on your actual configuration.** `GpuBenchmarks` is parameterised across hidden width
specifically so the crossover can be read off rather than guessed:

```bash
dotnet run -c Release --project benchmarks/RLNet.Benchmarks -- --filter '*GpuBenchmarks*'
```

## How it decides

Every call carries a work estimate, and anything below `MinimumWorkPerCall` runs on the CPU path
instead of paying for a transfer:

```csharp
if ((long)batch * inputSize * outputSize < MinimumWorkPerCall)
{
    _fallback.DenseForward(...);   // CPU
    return;
}
```

The threshold defaults to 2²⁰ multiply-accumulates and is deliberately high. This matters most for
**action selection**, which is a single-observation forward pass on every environment step: sending
that to the device would make choosing an action slower than the entire rest of the training loop.

Lower it to 0 to force everything onto the device (which is what the benchmark does, so the small
cases measure the GPU rather than the CPU twice).

## Falling back

A machine without a GPU is the common case, and it should mean "run on the CPU", not "crash on
startup":

```csharp
using var backend = GpuComputeBackend.TryCreate();   // never throws
Console.WriteLine(backend.Name);
// "GPU (Cuda: NVIDIA GeForce RTX 4060)"  or  "CPU SIMD (256-bit vectors)"
```

`new GpuComputeBackend()` throws `NotSupportedException` when no accelerator is present. Use it only
when a GPU is a hard requirement and you want to fail loudly.

To see what is available without starting a run:

```bash
dotnet run -c Release --project benchmarks/RLNet.Benchmarks -- --devices
```

## What runs on the device

Exactly the three matrix products a dense layer decomposes into, plus the activation:

| Kernel | Threads | What it computes |
|---|---|---|
| `ForwardKernel` | batch × outputSize | `activation(x · W + b)` |
| `GradInputKernel` | batch × inputSize | `dL/dx = gradOut · Wᵀ` |
| `GradWeightKernel` | inputSize × outputSize | `dL/dW += xᵀ · gradOut` |
| `GradBiasKernel` | outputSize | `dL/db += Σ gradOut` |
| `ActivationBackwardKernel` | batch × outputSize | folds the nonlinearity's derivative in |

`GradWeightKernel` gives each thread **one weight** and sums over the batch itself, so no two
threads ever write the same element and the kernel needs no atomics — which is what makes it scale.

The nonlinearity is part of the backend contract rather than applied afterwards by the caller,
because a device backend wants to fuse it into the same kernel; making it a separate step would
force a round trip per layer and give back most of what the accelerator won.

## ILGPU

ILGPU is fully managed: it compiles kernels to PTX (CUDA) or OpenCL C **at runtime**, from the same
C# source. So `RLNet.Gpu` stays cross-platform and ships no native binaries of its own beyond the
driver already on the machine.

Kernels must stay inside the subset ILGPU can compile — no allocations, no exceptions, no closures,
no `MathF` (use `XMath` from `ILGPU.Algorithms`).

Device buffers are cached and grown rather than allocated per call. An allocation on an accelerator
is a synchronising operation, and doing one per gradient step would cost more than the kernels save.
Shapes repeat across a training run, so after the first few calls this stops allocating entirely.

## Correctness

A second implementation of the same arithmetic is a second place for it to be wrong, and a GPU
kernel that is subtly wrong produces an agent that trains slightly worse — invisible without a
comparison.

`GpuBackendTests` runs identical input through both backends and requires agreement to 1e-3 relative:

```csharp
CpuComputeBackend.Instance.DenseForward(weights, biases, input, cpuOutput, ...);
gpu.DenseForward(weights, biases, input, gpuOutput, ...);
AssertClose(cpuOutput, gpuOutput);
```

Exact equality is not achievable — float summation order differs between a serial CPU loop and a
parallel kernel — but the tolerance is tight enough that a transposed index or a missing term is
orders of magnitude outside it.

The tests **skip themselves** when no accelerator is present, which is the normal case on a CI
runner. Each one asserts it really got an accelerator first, so a skipped test is honest rather than
a test that silently passes by running the CPU path twice.

## Limitations

- `float` only. No mixed precision, no tensor cores.
- One accelerator. No multi-GPU.
- Dense layers only — the same scope as the CPU backend.
- No CUDA graph capture or stream overlap; every call synchronises.

The last one is the ceiling on what this can currently reach. A version that pipelines kernel
launches would do better on large networks; it is not there yet.

## Next

- [Neural engine](05-neural-network.md) — what the backend sits under
- [Benchmarks](09-benchmarks.md) — where the crossover actually is
