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
network does. At those sizes [`CpuComputeBackend`](05-neural-network.md) generally wins outright.

**Where the CPU stops winning depends entirely on the device**, so there is no single crossover
number to quote. Here is what `GpuBenchmarks` measured on the development machine — a dense forward
pass at a batch of 256, on an *integrated* Intel UHD 620 over OpenCL:

| Hidden width | CPU SIMD | GPU (Intel UHD 620) | GPU is |
|---:|---:|---:|---|
| 64 | 338 µs | 2,802 µs | **8.3× slower** |
| 256 | 3,932 µs | 8,424 µs | **2.2× slower** |
| 1,024 | 75,070 µs | 112,921 µs | **1.5× slower** |

The GPU never wins on this hardware — but notice the gap closing steadily as the network widens
(8.3× → 2.2× → 1.5×), which is exactly the transfer overhead being amortised over more arithmetic.
An integrated GPU shares memory bandwidth with the CPU it is meant to be beating, so this is close
to the worst case; a discrete card with its own memory would cross over far earlier. The shape of
the trend is the transferable part, not the numbers.

Where it does pay off:

- Wide networks, on a discrete GPU — the trend above extrapolates to a win, this hardware just never
  reaches it
- Large-batch offline updates
- Image-like observations, if you extend the library that far
- Sweeps training many agents at once

**Measure it on your hardware.** `GpuBenchmarks` is parameterised across hidden width (64, 256,
1024 at a batch of 256) specifically so the crossover can be read off rather than guessed:

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
- [Benchmarks](09-benchmarks.md) — what the CPU backend achieves, and how to run the GPU comparison
