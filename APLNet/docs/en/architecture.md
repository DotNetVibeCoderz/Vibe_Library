# Architecture

[Bahasa Indonesia](../id/architecture.md) · [Index](README.md)

## Layers

```
┌──────────────────────────────────────────────┐
│  Ergonomic API    Apl.For / ForEach / Reduce │  delegate overloads, TPL call shape
├──────────────────────────────────────────────┤
│  Struct invoker   IWorkBody, IRangeWorkBody, │  generic over your struct:
│                   IWorkBody<T>, IReduceBody  │  the JIT specialises and inlines
├──────────────────────────────────────────────┤
│  Scheduling       ParallelExecutor, pooled   │  partition once, queue without Task
│                   jobs, IPartitioner         │  or ExecutionContext, caller works too
├──────────────────────────────────────────────┤
│  SIMD kernels     SimdOps, operator structs  │  Vector512/256/128, scalar tail
├──────────────────────────────────────────────┤
│  Unsafe tier      UnsafeParallel, NativeBuf. │  pointers, delegate*, no bounds checks
└──────────────────────────────────────────────┘
```

| Namespace | Contents |
|---|---|
| `AplNet` | `Apl`, `AplOptions`, `[AplBody]`, `[AplRangeBody]` |
| `AplNet.Core` | Body interfaces, `ParallelExecutor`, `IParallelismProvider`, the job engine (internal) |
| `AplNet.Core.Partitioners` | `IPartitioner`, `StaticRangePartitioner`, `StripedPartitioner`, `WorkStealingPartitioner` |
| `AplNet.Simd` | `SimdOps`, `SimdCapabilities`, operator interfaces and built-in operators |
| `AplNet.Unsafe` | `UnsafeParallel`, `NativeBuffer<T>` |
| `AplNet.Diagnostics` | `AplEventSource` |

## One shape inside

Every public loop is adapted to one internal shape, `IRangeWorkBody` ("run
`[from, to)`"). A few small `struct` adapters do the conversion:

- `ElementBody<TBody>` loops over `IWorkBody.Invoke(i)`.
- `DelegateBody` loops over an `Action<int>`.
- `ArrayItemBody<T, TBody>` loops over a span of the array and passes each element by `ref`.
- The SIMD operations wrap a kernel call per slice.

Because every adapter is a struct, the JIT specialises straight through it into your body. The
only runtime dispatch left is one virtual call per range, from the job to its body.

## One call, step by step

1. **Plan** (`ParallelExecutor.TryPlan`). The executor validates the range, checks the token,
   and reads the degree of parallelism: `AplOptions.MaxDegreeOfParallelism`, or else
   `IParallelismProvider`, which defaults to `Environment.ProcessorCount`. It caps the number of
   workers at `ceil(n / MinChunkSize)` and asks the partitioner how many partitions to make.
   With a single worker, the loop runs **inline** on the caller, with no job and no queueing.
2. **Rent a job.** Each body type has a small lock-free pool of jobs (`RangeJob<TBody>`,
   `ReduceJob<T, TBody>`). In steady state, renting and returning a job allocates nothing.
3. **Queue.** The job is itself an `IThreadPoolWorkItem`. It is queued `workers - 1` times
   with `ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false)`. That call creates no
   `Task`, no closure and no captured context.
4. **Work.** The caller becomes one of the workers. How workers get their share depends on the
   partitioner:
   - **Static or striped:** each worker claims the next *partition* with one `Interlocked.Increment`.
     That is one increment per partition, never one per iteration.
   - **Work stealing:** each worker claims a *slot*. It takes grains from the front of its own
     range with CAS, and steals the back half of the fullest other range when its own runs dry.
5. **Join.** Workers count finished partitions (or iterations, when stealing). The last one to
   finish sets a `ManualResetEventSlim` on the job. The caller spins briefly, then waits on it.
   **The caller waits only for work that has actually started, never for a queued item to be
   picked up.** If the pool is busy, the caller claims the partitions itself. That is why nested
   loops cannot deadlock.
6. **Release.** The job holds a reference count: one for the caller and one per queued item. A
   queued item that runs after the loop has already returned finds nothing to claim and releases
   its reference. When the last reference is released, the job clears the body (so it keeps no
   references alive) and goes back to its pool.
7. **Outcome.** Exceptions thrown by bodies are collected, and one `AggregateException` is
   thrown after every running worker has stopped. Cancellation is observed between blocks of
   `CancellationCheckInterval` iterations and surfaces as `OperationCanceledException`, as in TPL.

## Why the inner loop is fast

For each `(from, to)` it runs, the job copies the body struct into a local variable. The JIT
can then keep the body's fields (array references, scalars) in registers for the whole inner
loop. A mutable body also cannot race with another worker through a shared field.

For `Apl.For(0, n, new Affine(src, dst))`, the JIT's actual output is this inner loop, with
bounds checks hoisted by loop cloning:

```asm
G_M000_IG04:
       mov      r10d, edx
       vmulsd   xmm2, xmm0, qword ptr [rax+8*r10+0x10]
       vaddsd   xmm2, xmm2, xmm1
       vmovsd   qword ptr [rcx+8*r10+0x10], xmm2
       inc      edx
       cmp      edx, r8d
       jl       SHORT G_M000_IG04
```

The delegate overload runs the same loop with a `call [Action<int>.Invoke]` per element.

## The rest of the repository

```
src/APL.Net/                 the library (package "APL.Net", namespace AplNet)
src/APL.Net.SourceGen/       the Roslyn generator, shipped inside the package
tests/APL.Net.Tests/         xunit.v3 suite (JIT)
tests/APL.Net.AotTests/      console checks, published with PublishAot=true
benchmarks/APL.Net.Benchmarks/  BenchmarkDotNet B1–B7
benchmarks/rust-rayon/       out-of-band Rust/Rayon reference (no FFI)
samples/APL.Net.Samples/     console quick start + image processing demo
samples/APL.Net.Gallery/     Avalonia showcase comparing APL.Net and TPL
docs/en, docs/id             this documentation, in English and Indonesian
```
