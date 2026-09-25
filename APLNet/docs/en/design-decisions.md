# Design decisions

[Bahasa Indonesia](../id/design-decisions.md) · [Index](README.md)

This is the most important document for anyone changing APL.Net. Each decision below trades
something away on purpose. Decisions written down without their reasons tend to get "fixed" by
well-meaning contributors, and those fixes put back exactly the overhead this library exists to
remove.

## 1. Static partitioning is the default

**Decision.** By default, the range is split once, before any work starts, into one contiguous
slice per worker. The slices are balanced so their sizes differ by at most one item.
Workers claim whole partitions with one `Interlocked.Increment` each. Nothing is claimed per
iteration, and nothing is claimed per chunk.

**Why.** `Parallel.For`'s default partitioner hands out chunks dynamically. Every chunk is a
compare-and-swap on a counter shared by every core, plus the bookkeeping around it. For bodies
that cost a nanosecond or two, that dominates. Static splitting pays for scheduling once per
call.

**The cost, accepted and documented.** If some iterations cost far more than others, the worker
that owns them finishes last and the rest sit idle. Benchmark B6 and the Gallery's *Mandelbrot*
and *Zipf* cases show this openly: with the default partitioner, `Parallel.For` wins those.
Static splitting is also more sensitive to a single slow core (a descheduled thread, or a
hyper-threaded sibling under load), because no one can pick up its slice.

**What not to do.** Do not make the default partitioner dynamic, and do not quietly fall back to
dynamic claiming when a loop "looks uneven". That would put TPL's per-chunk cost back into every
loop, including the loops that don't need it. The fix is opt-in:
[`WorkStealingPartitioner`](partitioners.md) or `StripedPartitioner`. Contributions that improve
load balance belong there.

**Balanced, not `ceil(n / dop)`.** The spec sketched `chunk = ceil(n / dop)`. With 9 items and 8
workers, that gives five workers two items each and leaves three idle. The balanced split gives
one worker two items and the other seven one each. The cost is the same, and the result is
strictly better.

## 2. `UnsafeQueueUserWorkItem`: no ExecutionContext flow

**Decision.** Workers are queued with `ThreadPool.UnsafeQueueUserWorkItem(IThreadPoolWorkItem, false)`.

**Why.** `Task.Run` and `Parallel.For` capture the caller's `ExecutionContext` and restore it on
every worker. For a numeric loop that never reads `AsyncLocal<T>` or the culture, that is pure
cost.

**The consequence.** A body running on a pool worker does not see the caller's `AsyncLocal<T>`
values or flowed culture/impersonation. Iterations the caller runs itself *do* see them, so
behaviour varies with which thread gets which slice. A test pins this
(`ExecutionContextDoesNotFlowToPoolWorkers`). If a body needs ambient state, pass it in as a field
of the body struct. The async wrappers (`ForEachAsync`, `ForAsync`) are built on TPL and *do*
flow the context. They are I/O-shaped APIs, where dispatch cost doesn't matter.

## 3. Pooled jobs instead of `CountdownEvent`

**Decision.** The spec sketched a `CountdownEvent` plus a closure per chunk, and flagged pooling
as a v1.1 follow-up. Pooling was done from the start. Each loop in flight is a pooled
`RangeJob<TBody>`, which is itself the `IThreadPoolWorkItem`, joined with a
`ManualResetEventSlim` that belongs to the job.

**Why.** A `CountdownEvent`, a closure per chunk and a `Task` are each an allocation on every
call. For many small loops (a simulation step, a per-frame update), the allocations and the GC
work they cause cost more than the loop itself. Measured: **0 B** per call on the struct path,
versus about 3 KB for `Parallel.For` ([B7](benchmark-results.md)). In the Gallery's *Many small
loops* case, 200 loops of 2,000 elements allocate 38 B in total, versus about 590 KB.

**The subtle part: late work items.** The caller never waits for a queued item to be dequeued.
It waits only for partitions that are running, and claims the rest itself. So an item can run
*after* its loop has returned. A reference count (one per queued item plus one for the caller)
makes sure the job is returned to the pool only when the last holder lets go. A late item finds
no partition left to claim and simply releases its reference. **Do not** "simplify" this by
waiting for every queued item: under a busy pool or nested loops, that turns into a deadlock.

## 4. Devirtualisation is verified, not assumed

The struct-invoker claim, that there is no dispatch per element, depends on the JIT inlining
`Invoke`. It is not enough for it to devirtualise the call. This was checked by dumping the JIT's
output (`DOTNET_JitDisasm="InvokeRange"`, `DOTNET_TieredCompilation=0`) for
`RangeJob<ElementBody<Affine>>.InvokeRange`. The hot loop contains no call:

```asm
       vmulsd   xmm2, xmm0, qword ptr [rax+8*r10+0x10]
       vaddsd   xmm2, xmm2, xmm1
       vmovsd   qword ptr [rcx+8*r10+0x10], xmm2
       inc      edx
       cmp      edx, r8d
       jl       SHORT G_M000_IG04
```

The same dump for the delegate path shows `call [Action<int>.Invoke]` inside the loop. To
re-check after changing the executor:

```sh
DOTNET_TieredCompilation=0 DOTNET_JitDisasm="InvokeRange" \
  dotnet tests/APL.Net.AotTests/bin/Release/net10.0/APL.Net.AotTests.dll
```

Two details keep this true:

- The job copies the body into a local before looping. The JIT can then keep the body's fields
  in registers, and a non-readonly body is not defensively copied on every call.
- The adapters (`ElementBody`, `ArrayItemBody`) are structs marked `AggressiveInlining`.

## 5. The caller works too

The calling thread would otherwise just block. Instead it takes a share of the work. That
removes one wake-up from the critical path. It also means a loop always makes progress, even
when every pool thread is busy, which is what makes nested `Apl.For` calls safe.

## 6. Spans never cross threads

A `Span<T>` cannot be captured or stored in a field. Every parallel API therefore takes `T[]`,
`Memory<T>` or `ReadOnlyMemory<T>` at the point where work is handed to other threads, and turns
the data back into a span only inside one worker's slice. The spec considered pinning and passing
raw pointers, and preferred the `Memory<T>` route. APL.Net follows that preference. Raw pointers
live only in the opt-in unsafe tier.

## 7. SIMD through operator structs, not delegates

`SimdOps` is generic over operator structs (`IUnaryOperator<T>`, `IBinaryOperator<T>`,
`IReduceOperator<T>`). Each operator states its operation once for scalars and once per vector
width. Two alternatives were rejected:

- **Default interface methods** could derive the wider forms from the narrower ones. But a DIM
  called on a struct goes through a boxed copy, which is exactly the cost this is avoiding.
- **Delegates** (`Func<Vector256<T>, Vector256<T>>`) are offered for convenience, because the
  spec lists them. They cost one call per vector and are limited to 256-bit vectors.

Reductions keep four independent vector accumulators, because the latency of a floating-point
add would otherwise cap a sum at one vector every four cycles. For floating-point types, the
result therefore matches a sequential sum only up to rounding. Integer results match exactly,
including overflow wrap-around.

## 8. `stackalloc` is bounded, or not used

The spec's `SumParallelSimd` sketch used `dop <= 64 ? stackalloc : new`. APL.Net does not need
either: each reduction's partial results live in a pooled array on its job, spaced a cache line
apart so workers don't falsely share. No per-call buffer is created at all.

## 9. Exceptions from the unsafe tier are aggregated too

The spec described the pointer tier as failing fast on a worker exception. APL.Net routes it
through the same executor instead, so a managed exception thrown by a function-pointer body
arrives as an `AggregateException`, like everywhere else. A `try` region costs nothing until
something throws, so choosing crash-the-process over report-the-error bought no speed. An
access violation still cannot be caught; that was never optional.

## 10. Naming

The product and package are **APL.Net**. The .NET naming guidelines Pascal-case acronyms of three
letters or more, and the owner asked for standard C# conventions. So the namespace is `AplNet` and
the entry point is `Apl`: `Apl.For(...)`, not `APL.For(...)`. There is a practical reason too:
a type named `APL` inside a namespace `APL` would make every `APL.For` resolve to the namespace
and fail to compile.

## 11. What was added beyond the spec, and why

| Addition | Reason |
|---|---|
| `IRangeWorkBody` / `Apl.ForRange` | The fastest shape: one call per slice, spans without bounds checks, room for SIMD |
| `Apl.Reduce` with `IReduceBody<T>` | Reductions are the most common thing people get wrong with `Parallel.For` (locks, `Interlocked`) |
| `MinChunkSize` | Stops tiny loops from being split into slices smaller than the cost of waking a thread |
| `FixedParallelismProvider` | Makes scheduling deterministic in tests (NFR5) |
| `NativeBuffer<T>` | Aligned native memory for the pointer tier and for SIMD |
| `SimdCapabilities` | Tells the user which width the kernels will actually use |
| NativeAOT check app | xunit's runner relies on reflection, so the AOT half of the definition of done needs its own binary |
