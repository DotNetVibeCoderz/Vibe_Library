# APL.Net — Another Parallel Library for .NET

**Parallel loops without the per-iteration tax.** APL.Net provides data-parallel loops for .NET
that the JIT compiles for your exact body. There is no delegate call, no `Task`, no captured
`ExecutionContext` and no allocation per call. It also includes explicit SIMD kernels
(Vector512/256/128 with a scalar fallback), an opt-in pointer tier and a source generator. It is
NativeAOT-safe and uses no native interop.

Made by **Gravicode Studios**, led by **Kang Fadhil**. · Dibuat oleh Gravicode Studios, dipimpin
oleh Kang Fadhil.

```csharp
using AplNet;
using AplNet.Core;
using AplNet.Simd;

Apl.For(0, n, i => result[i] = Compute(data[i]));             // drop-in for Parallel.For

readonly struct ComputeBody(double[] data, double[] result) : IWorkBody
{
    public void Invoke(int i) => result[i] = Compute(data[i]);
}
Apl.For(0, n, new ComputeBody(data, result));                 // zero-allocation, inlined

double total = SimdOps.ParallelSum(values);                    // parallel + SIMD
Apl.For(0, rows, new RenderRow(img),                           // uneven work: opt in
    new AplOptions { Partitioner = new WorkStealingPartitioner(1) });
```

Measured on an Intel i7-8650U (4C/8T) with .NET 10 against `Parallel.For`:

- **3.7× lower** per-call overhead, with **0 B** allocated (versus ~3 KB).
- **4.4×** on in-cache arithmetic.
- **19×** on SIMD AXPY (1M floats).
- **5.7×** on a sum (1M doubles).

There are also results where APL.Net does not win. On memory-bound loops every variant ties,
and with the default static partitioning, skewed workloads can favour TPL. Both are documented.

⚠️ The `ExecutionContext` does **not** flow to pool workers (`AsyncLocal<T>` and culture are not
visible inside bodies that run on other threads).

Documentation (English and Indonesian), benchmark results, the Avalonia **APL.Net Gallery** and
the design rationale: https://github.com/DotNetVibeCoderz/Vibe_Library/tree/main/APLNet
