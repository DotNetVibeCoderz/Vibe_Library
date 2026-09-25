# APL.Net — Another Parallel Library (for .NET)

> High-performance data-parallel primitives for .NET, built without native interop,
> targeting throughput closer to Rust+Rayon by eliminating TPL's per-iteration overhead
> (delegate dispatch, `ExecutionContext` capture, bounds checks) and adding manual SIMD.

**Status:** Draft specification for implementation
**Target framework:** .NET 8 / .NET 9 (requires `UnmanagedCallersOnly`-era JIT, `Vector256/512`, `NativeMemory`)
**License:** TBD (suggest MIT)

---

## 1. Background & Motivation

`System.Threading.Tasks.Parallel` (`Parallel.For`, `Parallel.ForEach`) is a general-purpose
scheduler optimized for *arbitrary* workloads. That generality costs overhead per iteration:

| Overhead source | Cost | APL.Net mitigation |
|---|---|---|
| Dynamic range partitioning (interlocked chunk claiming) | lock/CAS per chunk | Static/striped partitioning computed once upfront |
| `Action<int>` / `Func<T>` delegate dispatch | virtual call, possible closure heap alloc | Generic `struct` constrained to interface → JIT devirtualizes & inlines |
| `Task` allocation + `ExecutionContext` flow | alloc + capture/restore per task | `ThreadPool.UnsafeQueueUserWorkItem`, no ambient context flow |
| Array bounds checking | branch per access | `unsafe` pointers / `Span<T>` with proven bounds elided by JIT |
| No SIMD | 1 scalar op/cycle | Manual `Vector256<T>`/`Vector512<T>` (or portable `Vector<T>`) chunking |

None of this requires native code. The performance gap with Rust is **not** managed-vs-native
per se (RyuJIT + PGO + tiered compilation is close to native for hot loops) — it's these
scheduling/dispatch layers. APL.Net targets exactly those layers.

### Non-goals (v1)
- Distributed (multi-machine) execution — out of scope; see §9 for future direction.
- GPU offload.
- Automatic vectorization/transpilation of arbitrary lambdas (user must opt into SIMD-friendly APIs).

---

## 2. Requirements

### 2.1 Functional Requirements

- **FR1**: Provide `APL.For(int from, int to, TBody body)` and `APL.ForEach<TSource>(...)` with
  TPL-like call ergonomics, but generic-struct-based (no delegate boxing required, delegate
  overload still available for ergonomics/back-compat).
- **FR2**: Provide SIMD-accelerated bulk operations over `Span<T>`/arrays for common numeric ops
  (map, map-reduce, transform-in-place) for `float`, `double`, `int`, `long`.
- **FR3**: Support cancellation (`CancellationToken`) and exception aggregation (`AggregateException`)
  compatible with existing TPL exception-handling patterns.
- **FR4**: Support configurable degree of parallelism (`MaxDegreeOfParallelism`), defaulting to
  `Environment.ProcessorCount`.
- **FR5**: Provide an `unsafe`/pointer-based low-level API (`APL.Unsafe`) for advanced users who
  want to skip all bounds/range safety.
- **FR6**: Provide a partitioner abstraction pluggable per call: `StaticRangePartitioner` (default),
  `StripedPartitioner`, `WorkStealingPartitioner` (v2).
- **FR7**: Zero mandatory heap allocation in the steady-state hot path when using the struct-body API.
- **FR8**: Must compose with `NativeAOT` publish (no reflection-based dispatch in hot path).

### 2.2 Non-Functional Requirements

- **NFR1 (Performance)**: For granular numeric workloads (≤50ns of work per element), APL.Net
  must show ≥2x throughput vs `Parallel.For` on identical hardware, measured via BenchmarkDotNet.
- **NFR2 (Safety)**: Default API surface (`APL.For`/`APL.ForEach`) must remain memory-safe;
  `unsafe` is opt-in via a clearly separated namespace (`APL.Unsafe`).
- **NFR3 (Portability)**: SIMD paths must degrade gracefully (scalar fallback) on hardware without
  `Vector256`/`Vector512` support, detected via `Vector.IsHardwareAccelerated` /
  `Avx2.IsSupported` / `Avx512F.IsSupported`.
- **NFR4 (Compatibility)**: Must run under both JIT and NativeAOT; no `System.Reflection.Emit`.
- **NFR5 (Testability)**: All partitioning/scheduling logic must be unit-testable independent of
  actual hardware core count (inject `IParallelismProvider`).
- **NFR6 (Observability)**: Optional lightweight counters (chunks executed, steal count if
  work-stealing enabled) exposed via `EventSource` for diagnostics, off by default (no cost when
  disabled).

---

## 3. Feature List

### v1.0 (MVP)
- [ ] `APL.For<TBody>(int from, int to, TBody body)` — struct-invoker range loop
- [ ] `APL.For(int from, int to, Action<int> body)` — delegate overload (ergonomic, TPL drop-in)
- [ ] `APL.ForEach<TSource, TBody>(TSource[] source, TBody body)`
- [ ] `ParallelOptions`-equivalent: `AplOptions { MaxDegreeOfParallelism, CancellationToken }`
- [ ] `APL.Unsafe.ForPtr(double* ptr, int len, delegate*<double*, int, void> body)` — pointer-based, no bounds checks
- [ ] SIMD ops: `APL.Simd.Transform(Span<float> data, Func<Vector256<float>, Vector256<float>> op)`
- [ ] SIMD ops: `APL.Simd.MapReduce(ReadOnlySpan<double> data, ...)` (sum/min/max/custom monoid)
- [ ] `StaticRangePartitioner` (chunk = ceil(n / degreeOfParallelism))
- [ ] Exception aggregation matching `System.AggregateException` semantics
- [ ] BenchmarkDotNet project comparing vs `Parallel.For`, plain sequential loop, and (optionally) a Rust/Rayon reference binary called out-of-band (not via FFI — separate benchmark executable)

### v1.1
- [ ] `StripedPartitioner` for workloads with locality-sensitive access patterns
- [ ] `APL.ForEachAsync` (async body support, thin wrapper, not a performance-critical path)
- [ ] `Vector512` (AVX-512) path where supported, auto-fallback to `Vector256`
- [ ] Source-generator that emits struct-invoker boilerplate from a lambda at compile time (ergonomics: user writes a lambda, generator emits the `IWorkBody` struct) — see §8

### v2.0 (exploratory)
- [ ] `WorkStealingPartitioner` (Chase-Lev deque) for uneven workloads
- [ ] `EventSource`-based diagnostics/ETW counters
- [ ] Experimental distributed worker mode (see §9)

---

## 4. Architecture & Design

### 4.1 Layering

```
┌─────────────────────────────────────────────┐
│  Ergonomic API (APL.For / APL.ForEach)       │  ← delegate overloads, drop-in for TPL
├─────────────────────────────────────────────┤
│  Struct-Invoker Core (IWorkBody<T> generic)  │  ← zero-alloc, JIT-devirtualized dispatch
├─────────────────────────────────────────────┤
│  Scheduling Layer (Partitioner + Executor)   │  ← StaticRangePartitioner, ThreadPool queueing
├─────────────────────────────────────────────┤
│  SIMD Kernels (APL.Simd)                     │  ← Vector256/512 map/reduce primitives
├─────────────────────────────────────────────┤
│  Unsafe Layer (APL.Unsafe)                   │  ← raw pointers, delegate*, no bounds checks
└─────────────────────────────────────────────┘
```

### 4.2 Core interfaces

```csharp
namespace APL.Core;

/// Implement as a `readonly struct` to get JIT devirtualization + inlining.
/// This is the mechanism that replaces delegate dispatch.
public interface IWorkBody
{
    void Invoke(int index);
}

/// For ForEach over a source array/span with per-element access.
public interface IWorkBody<T>
{
    void Invoke(int index, ref T item);
}

public interface IParallelismProvider
{
    int DegreeOfParallelism { get; }
}

public sealed class DefaultParallelismProvider : IParallelismProvider
{
    public int DegreeOfParallelism => Environment.ProcessorCount;
}
```

### 4.3 Scheduling: static partitioning + `UnsafeQueueUserWorkItem`

Key design decisions and why:

1. **Static range partitioning computed once, upfront** — no interlocked chunk-claiming per
   iteration like TPL's default dynamic partitioner. Trade-off: worse load balancing for
   very uneven workloads (mitigated in v2 by `WorkStealingPartitioner`).
2. **`ThreadPool.UnsafeQueueUserWorkItem`** instead of `Task.Run`/`Parallel.For` — skips
   `ExecutionContext` capture/restore, which is pure overhead when the body doesn't rely on
   ambient state (culture, `AsyncLocal`, impersonation). This is *unsafe* in the .NET sense
   (doesn't flow context) — acceptable trade-off, documented clearly.
3. **`ManualResetEventSlim` / `CountdownEvent` for join**, not `Task.WhenAll` — avoids Task
   object + continuation machinery for the common synchronous-wait case.

```csharp
namespace APL.Core;

public static class ParallelExecutor
{
    public static void For<TBody>(int from, int to, TBody body, AplOptions? options = null)
        where TBody : struct, IWorkBody
    {
        int n = to - from;
        if (n <= 0) return;

        int dop = options?.MaxDegreeOfParallelism ?? Environment.ProcessorCount;
        dop = Math.Min(dop, n);

        if (dop <= 1)
        {
            for (int i = from; i < to; i++) body.Invoke(i);
            return;
        }

        int chunk = (n + dop - 1) / dop;
        using var countdown = new CountdownEvent(dop);
        Exception? capturedException = null;
        object exceptionLock = new();

        for (int t = 0; t < dop; t++)
        {
            int start = from + t * chunk;
            int end = Math.Min(start + chunk, to);
            if (start >= end) { countdown.Signal(); continue; }

            // Capture body by value (struct copy — cheap, no heap allocation).
            var localBody = body;
            ThreadPool.UnsafeQueueUserWorkItem(state =>
            {
                try
                {
                    for (int i = start; i < end; i++)
                        localBody.Invoke(i);
                }
                catch (Exception ex)
                {
                    lock (exceptionLock) { capturedException ??= ex; }
                }
                finally
                {
                    countdown.Signal();
                }
            }, null, preferLocal: false);
        }

        countdown.Wait(options?.CancellationToken ?? default);

        if (capturedException is not null)
            throw new AggregateException(capturedException);
    }
}

public sealed class AplOptions
{
    public int? MaxDegreeOfParallelism { get; init; }
    public CancellationToken CancellationToken { get; init; }
}
```

> **Note for implementer:** `CountdownEvent.Wait(CancellationToken)` throws
> `OperationCanceledException` — decide whether in-flight work items should observe cancellation
> too (v1: cooperative check inside long loops via `token.ThrowIfCancellationRequested()` every
> N iterations, exposed as a parameter).

### 4.4 Ergonomic delegate overload (TPL drop-in)

```csharp
namespace APL;

public static class APL
{
    public static void For(int from, int to, Action<int> body, AplOptions? options = null)
        => ParallelExecutor.For(from, to, new DelegateBody(body), options);

    private readonly struct DelegateBody : Core.IWorkBody
    {
        private readonly Action<int> _body;
        public DelegateBody(Action<int> body) => _body = body;
        public void Invoke(int index) => _body(index);
    }

    // Zero-alloc path for users who define their own struct body:
    public static void For<TBody>(int from, int to, TBody body, AplOptions? options = null)
        where TBody : struct, Core.IWorkBody
        => Core.ParallelExecutor.For(from, to, body, options);
}
```

Usage comparison:

```csharp
// TPL
Parallel.For(0, n, i => result[i] = Compute(data[i]));

// APL.Net — ergonomic (same call shape, less overhead under the hood)
APL.For(0, n, i => result[i] = Compute(data[i]));

// APL.Net — zero-alloc struct-invoker (for hot paths where every ns counts)
APL.For(0, n, new ComputeBody(data, result));

readonly struct ComputeBody : APL.Core.IWorkBody
{
    private readonly double[] _data;
    private readonly double[] _result;
    public ComputeBody(double[] data, double[] result) { _data = data; _result = result; }
    public void Invoke(int i) => _result[i] = Compute(_data[i]);
}
```

### 4.5 SIMD kernels (`APL.Simd`)

```csharp
namespace APL.Simd;

public static class SimdOps
{
    /// In-place transform: data[i] = op(data[i]), vectorized where possible.
    public static void TransformInPlace(Span<float> data, Func<Vector256<float>, Vector256<float>> vecOp,
                                         Func<float, float> scalarFallback)
    {
        int i = 0;
        int width = Vector256<float>.Count; // 8
        if (Avx2.IsSupported)
        {
            for (; i <= data.Length - width; i += width)
            {
                var v = Vector256.Create(data.Slice(i, width));
                var r = vecOp(v);
                r.CopyTo(data.Slice(i, width));
            }
        }
        for (; i < data.Length; i++)
            data[i] = scalarFallback(data[i]);
    }

    /// Parallel + SIMD map-reduce (e.g., sum). Splits into per-thread chunks,
    /// each chunk vector-reduced, then scalar-combined.
    public static double SumParallelSimd(ReadOnlySpan<double> data, AplOptions? options = null)
    {
        int dop = options?.MaxDegreeOfParallelism ?? Environment.ProcessorCount;
        int n = data.Length;
        if (n == 0) return 0.0;
        dop = Math.Min(dop, Math.Max(1, n / 1024)); // avoid over-splitting tiny arrays
        dop = Math.Max(dop, 1);

        Span<double> partials = dop <= 64 ? stackalloc double[dop] : new double[dop];
        int chunk = (n + dop - 1) / dop;

        // NOTE: capturing `data` (a ReadOnlySpan) in a closure across threads is illegal in C#
        // (ref struct). Implementer must convert to an unsafe pointer + length pair before
        // dispatching to ThreadPool, or use `MemoryMarshal`/pinned arrays. See implementation
        // notes in §7.
        // ... implementation detail elided here, see full source in repo skeleton ...
        return 0.0; // placeholder — replace with real reduction
    }
}
```

> **Design flag for implementer**: `ReadOnlySpan<T>` cannot cross thread boundaries directly
> (it's a `ref struct`). The real implementation must either (a) pin the source array and pass
> a raw pointer + length into each work item, or (b) require `Memory<T>` instead of `Span<T>`
> in the parallel-facing API and only use `Span<T>` inside each single-threaded chunk. **Prefer
> (b)** — cleaner and still allows `Span<T>` SIMD access per-chunk.

### 4.6 Unsafe low-level API (`APL.Unsafe`)

```csharp
namespace APL.Unsafe;

public static unsafe class UnsafeParallel
{
    /// Fully unsafe: caller guarantees ptr..ptr+len is valid for the duration of the call,
    /// no bounds checking, no exception marshaling across threads (fail-fast on unhandled
    /// exception in worker thread by design — this is the "you asked for it" tier).
    public static void ForPtr(double* ptr, int len, delegate*<double*, int, void> body,
                               int? maxDegreeOfParallelism = null)
    {
        int dop = maxDegreeOfParallelism ?? Environment.ProcessorCount;
        dop = Math.Min(dop, Math.Max(1, len));
        int chunk = (len + dop - 1) / dop;
        var countdown = new CountdownEvent(dop);

        for (int t = 0; t < dop; t++)
        {
            int start = t * chunk;
            int end = Math.Min(start + chunk, len);
            if (start >= end) { countdown.Signal(); continue; }
            double* localPtr = ptr;

            ThreadPool.UnsafeQueueUserWorkItem(_ =>
            {
                for (int i = start; i < end; i++)
                    body(localPtr, i);
                countdown.Signal();
            }, null, preferLocal: false);
        }
        countdown.Wait();
        countdown.Dispose();
    }
}
```

`delegate*<...>` (function pointer) avoids delegate allocation entirely — this is the closest
C# gets to a raw C function pointer while remaining verifiable-ish. Requires the callee to be a
static method (no closures).

---

## 5. Benchmark Plan

### 5.1 Environment

Document in README: CPU model, core/thread count, .NET SDK version, OS, whether run on battery/
power-plan (throttling skews results), `DOTNET_TieredPGO`/`DOTNET_ReadyToRun` env vars used.

### 5.2 Benchmark matrix

Use **BenchmarkDotNet** with `[SimpleJob(RuntimeMoniker.Net80)]` and
`[MemoryDiagnoser]` (to prove zero/low allocation claims).

| Benchmark | Element count | Work per element | Competing baselines |
|---|---|---|---|
| B1: Trivial arithmetic | 10K / 1M / 100M | `x * 2 + 1` (≈1-5ns) | sequential `for`, `Parallel.For`, `APL.For` delegate, `APL.For` struct-invoker |
| B2: Medium compute | 1M | `Math.Sqrt(x) * Math.Sin(x)` (≈20-50ns) | same as above |
| B3: Heavy compute | 100K | simulate 500ns+ work (e.g. small matrix mult per element) | same as above |
| B4: SIMD-friendly transform | 1M / 10M floats | `x * a + b` (AXPY-style) | scalar sequential, `Parallel.For`, `APL.Simd.TransformInPlace` |
| B5: Reduction (sum) | 1M / 10M doubles | sum | LINQ `.Sum()`, `Parallel.For` + lock/Interlocked accumulate, `APL.Simd.SumParallelSimd` |
| B6: Uneven workload | 10K (workload size follows a skewed distribution, e.g. Zipf) | variable | `Parallel.For` (dynamic partitioner should win here), `APL.For` (static partitioner — expected to lose; documents the trade-off honestly) |
| B7: Allocation check | N/A | N/A | `[MemoryDiagnoser]` output only — assert `Gen0` collections ≈ 0 for struct-invoker path |

### 5.3 Success criteria (ties to NFR1)

- B1/B2: APL.Net struct-invoker ≥2x throughput vs `Parallel.For` at 1M+ elements.
- B4/B5: APL.Simd ≥3x vs sequential scalar; ≥1.5x vs `Parallel.For` without SIMD.
- B6: Explicitly document if `Parallel.For` wins (expected) — this benchmark exists to be honest
  about static partitioning's weakness, not to be hidden.
- B7: Zero Gen0 allocations per `APL.For` struct-invoker call (excluding one-time `CountdownEvent`
  — consider pooling this in v1.1 if it shows up in the allocation profile).

### 5.4 Reference point vs Rust/Rayon (optional, out-of-band)

Do **not** FFI into Rust for this — build a **separate standalone Rust binary** with an
equivalent Rayon benchmark (using `criterion` crate) on the same machine, same data size, and
report both results side-by-side in the README as context, clearly labeled as
"different runtime, illustrative only, not apples-to-apples due to process startup/measurement
methodology differences."

---

## 6. Project Structure

```
APL.Net/
├── APL.Net.sln
├── src/
│   ├── APL.Net/                        # main library
│   │   ├── APL.cs                      # public ergonomic API
│   │   ├── AplOptions.cs
│   │   ├── Core/
│   │   │   ├── IWorkBody.cs
│   │   │   ├── ParallelExecutor.cs
│   │   │   ├── IParallelismProvider.cs
│   │   │   └── Partitioners/
│   │   │       ├── IPartitioner.cs
│   │   │       └── StaticRangePartitioner.cs
│   │   ├── Simd/
│   │   │   └── SimdOps.cs
│   │   └── Unsafe/
│   │       └── UnsafeParallel.cs
│   └── APL.Net.SourceGen/              # v1.1 — struct-body generator (optional)
├── tests/
│   └── APL.Net.Tests/
│       ├── ParallelExecutorTests.cs
│       ├── SimdOpsTests.cs
│       └── CorrectnessAgainstSequential.cs   # property-based: result must match sequential loop
├── benchmarks/
│   └── APL.Net.Benchmarks/
│       ├── Program.cs
│       ├── TrivialArithmeticBenchmarks.cs
│       ├── SimdBenchmarks.cs
│       └── UnevenWorkloadBenchmarks.cs
├── samples/
│   └── APL.Net.Samples/
│       ├── QuickStart.cs
│       └── ImageProcessingDemo.cs      # realistic use case: per-pixel transform
├── docs/
│   ├── getting-started.md
│   ├── design-decisions.md             # captures the trade-offs from §4 explicitly
│   └── benchmark-results.md            # generated from BenchmarkDotNet output, checked in
└── README.md
```

---

## 7. Implementation Notes for the Coding Agent

Read this before writing code — these are known traps:

1. **`ReadOnlySpan<T>`/`Span<T>` cannot be captured in a lambda or stored in a field** (they're
   `ref struct`). Any API that needs to hand data to a background thread must accept
   `T[]`/`Memory<T>` at the parallel-dispatch boundary, converting to `Span<T>` only inside the
   single-threaded chunk body.
2. **`CountdownEvent` allocates and has a `WaitHandle` under the hood** — for very high call
   frequency (e.g., called in a hot loop thousands of times/sec with small `n`), consider a
   pooled countdown primitive or a spin-wait fallback for small `dop`. Flag this as a v1.1
   perf follow-up, not a v1 blocker.
3. **`ThreadPool.UnsafeQueueUserWorkItem` doesn't flow `ExecutionContext`** — this is a deliberate
   trade-off (see §4.3) but must be documented loudly in XML doc comments and README, since it's
   a behavior difference from `Task.Run`/`Parallel.For` that could surprise users relying on
   `AsyncLocal` or culture flow inside the body.
4. **Devirtualization is not guaranteed** — JIT devirtualizes generic-struct-constrained interface
   calls only when it can prove the concrete type at the call site (monomorphization via generic
   specialization). Verify with disassembly (`DOTNET_JitDisasm` or BenchmarkDotNet's disassembly
   diagnoser) that `Invoke` is actually being inlined, not just devirtualized-but-called. This is
   the crux of the "zero-alloc, zero-dispatch-overhead" claim — must be empirically verified, not
   assumed.
5. **`stackalloc` for partials array (§4.5)** is only safe for small `dop` (bounded by
   `Environment.ProcessorCount`, realistically ≤256) — guard with the `dop <= 64 ? stackalloc : new`
   pattern shown, don't stackalloc unconditionally.
6. **Uneven workloads are a known, documented weakness** of static partitioning (B6 above) — do
   not try to "fix" this in v1 by secretly falling back to dynamic partitioning (that would
   silently reintroduce the overhead this library exists to avoid). Ship `WorkStealingPartitioner`
   as an explicit opt-in in v2 instead.
7. **NativeAOT compatibility**: avoid `Activator.CreateInstance`, reflection-based `Action`
   invocation paths, or anything requiring `DynamicallyAccessedMembers` trimming annotations in
   the hot path. The source generator (v1.1, §8) exists partly to avoid runtime reflection for
   the "user writes a lambda" ergonomic case.

---

## 8. Source Generator (v1.1 — ergonomics without sacrificing zero-alloc)

Problem: the struct-invoker API (`APL.For(0, n, new ComputeBody(...))`) is fast but verbose —
users must hand-write a struct per call site. A Roslyn source generator can let users write:

```csharp
[AplBody]
static void ComputeBody(int i, double[] data, double[] result) => result[i] = Compute(data[i]);

APL.For(0, n, AplGen.ComputeBody(data, result)); // generated struct + factory method
```

The generator emits a `readonly struct __ComputeBody_Generated : IWorkBody` capturing the
captured parameters as readonly fields, plus a factory method. This is purely a DX/ergonomics
feature — defer to v1.1, not required for MVP correctness or benchmark claims.

---

## 9. Distributed Execution (out of scope for v1, direction notes only)

If distributed (multi-machine) execution is wanted later, do **not** try to extend this library's
static-partitioning model directly across machines — the concerns are different (serialization,
partial failure, network I/O latency dominates over dispatch overhead). Recommended direction:

- Use APL.Net as the **intra-node** compute kernel on each worker.
- Use an existing mature .NET distributed runtime for **inter-node** orchestration — Orleans
  (virtual actors, good fit if workload is naturally partitionable into grains) or a
  message-queue-based worker pool (raw gRPC / Kafka / RabbitMQ) for simpler job-queue-style
  distribution.
- Do not build a custom RPC/serialization layer as part of APL.Net v1-v2 — that's a separate
  project with its own spec.

---

## 10. Documentation Deliverables Checklist

- [ ] `README.md` — quick pitch, install (`dotnet add package APL.Net`), quick-start snippet,
      link to benchmark results, honest "when NOT to use this" section (small `n`, uneven
      workloads, I/O-bound bodies — just use TPL).
- [ ] `docs/getting-started.md` — mirrors §4.4 examples, progressive: delegate → struct-invoker
      → SIMD → unsafe.
- [ ] `docs/design-decisions.md` — captures every trade-off in §4.3/§4.5/§7 in prose, so future
      contributors understand *why* static partitioning was chosen, why `UnsafeQueueUserWorkItem`,
      etc. This is the most important doc for long-term maintainability — decisions without
      rationale get "fixed" by well-meaning contributors who reintroduce the overhead.
- [ ] `docs/benchmark-results.md` — checked-in BenchmarkDotNet output (markdown export), regenerated
      per release, with hardware/SDK version header per §5.1.
- [ ] XML doc comments on every public API, especially loud warnings on `APL.Unsafe.*` and the
      `ExecutionContext`-flow behavior difference from TPL.
- [ ] `CONTRIBUTING.md` — note that PRs "fixing" load imbalance by adding dynamic claiming to the
      default partitioner should be redirected to the opt-in `WorkStealingPartitioner` path instead.

---

## 11. Definition of Done (v1.0 MVP)

- All FR1–FR8 and NFR1–NFR6 implemented or explicitly deferred with a linked issue.
- Benchmark suite (§5) runs green with results checked into `docs/benchmark-results.md`, meeting
  §5.3 success criteria on at least one reference machine (documented spec).
- Correctness test suite: for every parallel API, output must match a sequential reference
  implementation over randomized inputs (property-based, e.g. via `FsCheck` or hand-rolled
  randomized cases), including edge cases: `n=0`, `n=1`, `n < dop`, cancellation mid-run,
  exception thrown from one chunk.
- Package builds and its test suite passes under both JIT and `PublishAot` (NativeAOT) configurations.
- All items in §10 documentation checklist complete.

Notes:
- gunakan .NET 10
- Semua UI UX aplikasi buat yang keren dan user friendly dengan bantuan skill 'frontend-design'
- Untuk sample apps 'APL.Net  Gallery' dengan Avalonia UI Multiplatform, berisi berbagai contoh use case penggunaan, dibandingkan dengan TPL, terdapat sample code dan hasil benchmark di setiap case.
- optimasi koding agar dapat performa terbaik dan memory efisien
- gunakan naming convention standard c#
- readme dan docs dalam bahasa Indonesia dan English
- dokumentasi lengkap di folder docs
- Progress.md untuk tracking development, PLAN.md untuk roadmap pengembangan
- jika ada hal-hal yang penting perlu ditambahkan, silakan ditambahkan langsung biar lengkap.
- di aplikasi dan dokumentasi tambahkan informasi dibuat oleh Gravicode Studios dipimpin Kang Fadhil
- untuk publish nuget, api key ada di 'C:\Users\mifma\Documents\CodeSandbox\PackageCredentials.txt'
- screenshot-screenshot tambahkan pada dokumentasi dan readme

