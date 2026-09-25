# Getting started

[Bahasa Indonesia](../id/getting-started.md) · [Index](README.md)

## Install

```sh
dotnet add package APL.Net
```

APL.Net targets **.NET 10**. The package includes the source generator, so there is nothing
else to install. Namespaces follow the .NET naming guidelines: the product is *APL.Net*, the
namespace is `AplNet`, and the entry point is the static class `Apl`.

```csharp
using AplNet;           // Apl, AplOptions, [AplBody]
using AplNet.Core;      // IWorkBody, IRangeWorkBody, IReduceBody<T>
using AplNet.Simd;      // SimdOps and operator structs
```

The steps below go from least to most effort. Each one is faster than the one before it,
and each is optional. Step 1 alone already removes most of what `Parallel.For` spends on
scheduling.

## 1. The TPL shape: a drop-in for `Parallel.For`

```csharp
// TPL
Parallel.For(0, n, i => result[i] = Compute(data[i]));

// APL.Net: same call shape, less overhead underneath
Apl.For(0, n, i => result[i] = Compute(data[i]));
```

The delegate is still called for every element. What changes is everything around that call:

- The index range is split into equal slices up front, so workers never take turns
  claiming chunks through a shared counter.
- The work is queued without creating a `Task`.
- Each worker skips capturing and restoring the `ExecutionContext`.
- The calling thread works on a slice too, instead of just waiting.

> **Behaviour difference:** the `ExecutionContext` does not flow to pool workers. Values from
> `AsyncLocal<T>`, and the ambient culture, are not visible inside the body when it runs on
> another thread. If your body relies on ambient state, pass that state in explicitly. See
> [design decisions](design-decisions.md#2-unsafequeueuserworkitem-no-executioncontext-flow).

## 2. A struct body: no delegate at all

```csharp
readonly struct ComputeBody(double[] data, double[] result) : IWorkBody
{
    public void Invoke(int i) => result[i] = Compute(data[i]);
}

Apl.For(0, n, new ComputeBody(data, result));
```

`Apl.For<TBody>` is generic over your struct. For a struct type argument, the JIT compiles a
copy of the loop dedicated to that type. That lets it inline `Invoke`, so the result is the
loop you would have written by hand. The [design decisions](design-decisions.md#4-devirtualisation-is-verified-not-assumed)
page shows the machine code. Once the job pool is warm, a call allocates nothing.

## 3. A range body: whole slices, no bounds checks

```csharp
readonly struct ComputeRange(double[] data, double[] result) : IRangeWorkBody
{
    public void Invoke(int from, int to)
    {
        ReadOnlySpan<double> src = data.AsSpan(from, to - from);
        Span<double> dst = result.AsSpan(from, src.Length);
        for (int k = 0; k < src.Length; k++)
            dst[k] = Compute(src[k]);
    }
}

Apl.ForRange(0, n, new ComputeRange(data, result));
```

The body is called once per slice, not once per element. Because `k` is bounded by
`src.Length`, the JIT can drop the bounds checks. You can also hoist setup out of the loop, or
pass the slice to a SIMD kernel.

## 4. Reductions

```csharp
readonly struct SumOfSquares(double[] values) : IReduceBody<double>
{
    public double Accumulate(int from, int to, double acc)
    {
        foreach (double v in values.AsSpan(from, to - from))
            acc += v * v;
        return acc;
    }

    public double Combine(double a, double b) => a + b;
}

double total = Apl.Reduce(0, values.Length, 0.0, new SumOfSquares(values));
```

Each worker folds its own slices into a private accumulator. The accumulators are then
combined in partition order. There is no lock and no `Interlocked`.

## 5. SIMD

```csharp
SimdOps.ParallelTransformInPlace(samples, new MultiplyAddOperator<float>(gain, offset));
double sum  = SimdOps.ParallelSum(values);
float  dot  = SimdOps.ParallelDot(x, y);
```

Inside each worker's slice, the kernels use `Vector512`, `Vector256` or `Vector128`, whichever
the CPU accelerates, and fall back to scalar code when none is. See [SIMD](simd.md).

## 6. Uneven work: opt in to balancing

```csharp
var options = new AplOptions { Partitioner = new WorkStealingPartitioner(1) };
Apl.For(0, rows, new RenderRow(image), options);
```

Static partitioning is the default because it is the cheapest. When iterations vary widely in
cost, choose [work stealing or striping](partitioners.md).

## 7. The unsafe tier

```csharp
static unsafe void Scale(float* p, int from, int to)
{
    for (int i = from; i < to; i++) p[i] *= 0.5f;
}

using var buffer = new NativeBuffer<float>(n);                  // 64-byte aligned, off the GC heap
unsafe { UnsafeParallel.ForRangePtr(buffer.Pointer, n, &Scale); }
```

This tier has no bounds checks and no delegates. The caller is responsible for keeping the
memory valid for the whole call. See [unsafe tier](unsafe.md).

## 8. Or let the generator write the struct

```csharp
[AplBody]
internal static void Compute(int i, double[] data, double[] result) => result[i] = Compute(data[i]);

Apl.For(0, n, AplGen.Compute(data, result));
```

See [source generator](source-generator.md).

## Options

```csharp
var options = new AplOptions
{
    MaxDegreeOfParallelism = 4,               // default: Environment.ProcessorCount
    CancellationToken = token,                // checked between blocks
    CancellationCheckInterval = 4096,         // iterations per block when the token can cancel
    MinChunkSize = 10_000,                    // never give a worker fewer iterations than this
    Partitioner = new StripedPartitioner(64), // default: static ranges
};
```

Create one `AplOptions` and reuse it: options are immutable and safe to share across threads.

## Run the samples

```sh
dotnet run --project samples/APL.Net.Samples -c Release        # console quick start + image demo
dotnet run --project samples/APL.Net.Gallery -c Release        # the Avalonia gallery
```
