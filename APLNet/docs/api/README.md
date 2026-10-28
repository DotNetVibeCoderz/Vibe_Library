# APL.Net API reference

Generated from the XML documentation by `tools/APL.Net.ApiDocs`. Do not edit by hand: run
`dotnet run --project tools/APL.Net.ApiDocs -c Release -- docs/api` after changing a doc comment.
CI fails when this folder is out of date.

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

## AplNet

| Type | Kind | Summary |
|---|---|---|
| [Apl](AplNet.Apl.md) | class | APL.Net's entry point: data-parallel loops with the call shape of `Parallel` and far less overhead per iteration. |
| [AplBodyAttribute](AplNet.AplBodyAttribute.md) | class | Marks a static method as a loop body. The APL.Net source generator emits a `readonly struct` implementing [`IWorkBody`](AplNet.Core.IWorkBody.md) that calls it, plus a factory method on `AplNet.AplGen` named after the method. |
| [AplOptions](AplNet.AplOptions.md) | class | Per-call settings for APL.Net loops: the equivalent of `ParallelOptions`. |
| [AplRangeBodyAttribute](AplNet.AplRangeBodyAttribute.md) | class | Like [`AplBodyAttribute`](AplNet.AplBodyAttribute.md), for a method whose first two parameters are a range (`int fromInclusive, int toExclusive`). The generated struct implements [`IRangeWorkBody`](AplNet.Core.IRangeWorkBody.md) and is used with [`Apl.ForRange<T>`](AplNet.Apl.md). |

## AplNet.Core

| Type | Kind | Summary |
|---|---|---|
| [AplLoopResult](AplNet.Core.AplLoopResult.md) | struct | How a stateful loop ended: the equivalent of `ParallelLoopResult`. |
| [AplLoopState](AplNet.Core.AplLoopState.md) | struct | Lets an iteration end its loop early, like `ParallelLoopState`. Passed by value to each iteration of a stateful loop; it is two fields wide and allocates nothing. |
| [AplTopology](AplNet.Core.AplTopology.md) | class | The machine's NUMA nodes and logical processors, as [`SpinningWorkers`](AplNet.Core.SpinningWorkers.md) sees them when it places its threads. |
| [DefaultParallelismProvider](AplNet.Core.DefaultParallelismProvider.md) | class | Uses `Environment.ProcessorCount`. |
| [FixedParallelismProvider](AplNet.Core.FixedParallelismProvider.md) | class | Always reports the same degree of parallelism. |
| [ILongRangeWorkBody](AplNet.Core.ILongRangeWorkBody.md) | interface | A loop body invoked once per contiguous sub-range of a 64-bit range: the `long` counterpart of [`IRangeWorkBody`](AplNet.Core.IRangeWorkBody.md). |
| [ILongWorkBody](AplNet.Core.ILongWorkBody.md) | interface | A loop body invoked once per index of a 64-bit range: the `long` counterpart of [`IWorkBody`](AplNet.Core.IWorkBody.md), for loops over more than `Int32.MaxValue` items. |
| [IParallelismProvider](AplNet.Core.IParallelismProvider.md) | interface | Supplies the default degree of parallelism. Exists so scheduling can be tested independently of the machine it runs on. |
| [IRangeWorkBody](AplNet.Core.IRangeWorkBody.md) | interface | A loop body invoked once per contiguous sub-range rather than once per index. |
| [IReduceBody&lt;T&gt;](AplNet.Core.IReduceBody-1.md) | interface | A parallel reduction: each worker folds its ranges into a private accumulator, and the accumulators are then combined. |
| [IStatefulWorkBody](AplNet.Core.IStatefulWorkBody.md) | interface | A loop body that can end its loop early through [`AplLoopState`](AplNet.Core.AplLoopState.md): the struct form of `Parallel.For` with a `ParallelLoopState`. |
| [IWorkBody](AplNet.Core.IWorkBody.md) | interface | A loop body invoked once per index. Implement it on a `struct` (ideally a `readonly struct`) and pass it by its concrete type. |
| [IWorkBody&lt;T&gt;](AplNet.Core.IWorkBody-1.md) | interface | A `ForEach` body that receives each element by reference, so it can read or replace it in place without the copy a by-value delegate would make. |
| [ParallelExecutor](AplNet.Core.ParallelExecutor.md) | class | The struct-invoker core that every APL.Net loop runs on. [`Apl`](AplNet.Apl.md) is the friendlier entry point; these are the same operations without the delegate overloads. |
| [RefItemAction&lt;T&gt;](AplNet.Core.RefItemAction-1.md) | delegate | A `ForEach` callback that receives the element by reference. |
| [SpinningWorkers](AplNet.Core.SpinningWorkers.md) | class | An opt-in set of dedicated worker threads that keep spinning between loops instead of returning to the thread pool. Pass it in [`AplOptions.Workers`](AplNet.AplOptions.md). |
| [WorkerAffinity](AplNet.Core.WorkerAffinity.md) | enum | Where [`SpinningWorkers`](AplNet.Core.SpinningWorkers.md) pins its threads. |

## AplNet.Core.Partitioners

| Type | Kind | Summary |
|---|---|---|
| [IPartitioner](AplNet.Core.Partitioners.IPartitioner.md) | interface | Splits the index range `[0, length)` of a loop into partitions, once, before any work starts. |
| [StaticRangePartitioner](AplNet.Core.Partitioners.StaticRangePartitioner.md) | class | The default: one contiguous range per worker, all the same size to within one item. |
| [StripedPartitioner](AplNet.Core.Partitioners.StripedPartitioner.md) | class | Deals fixed-size stripes round-robin: worker `w` of `W` runs stripes `w, w + W, w + 2W, ...`. |
| [WorkStealingPartitioner](AplNet.Core.Partitioners.WorkStealingPartitioner.md) | class | Opt-in dynamic load balancing for loops whose iterations vary widely in cost. |

## AplNet.Diagnostics

| Type | Kind | Summary |
|---|---|---|
| [AplEventSource](AplNet.Diagnostics.AplEventSource.md) | class | Lightweight counters for APL.Net, published as EventCounters under the name `AplNet`. |

## AplNet.Simd

| Type | Kind | Summary |
|---|---|---|
| [AbsOperator&lt;T&gt;](AplNet.Simd.AbsOperator-1.md) | struct | `\|x\|`. |
| [AddOperator&lt;T&gt;](AplNet.Simd.AddOperator-1.md) | struct | `x + y`; as a reduction, the sum (identity 0). |
| [AddScalarOperator&lt;T&gt;](AplNet.Simd.AddScalarOperator-1.md) | struct | `x + addend`. |
| [ClampOperator&lt;T&gt;](AplNet.Simd.ClampOperator-1.md) | struct | `min(max(x, min), max)`. |
| [DivideOperator&lt;T&gt;](AplNet.Simd.DivideOperator-1.md) | struct | `x / y`. Integer division has no SIMD instruction on x86, so for integers this is correct but not fast. |
| [ExpOperator&lt;T&gt;](AplNet.Simd.ExpOperator-1.md) | struct | `e^x`, for `float` and `double`. The vector form is the runtime's own vectorized `Exp`: accurate to within a few ulp of `Math.Exp`, not bit-identical to it, so a vectorized result can differ from the scalar tail in the last digits. |
| [FusedMultiplyAddOperator&lt;T&gt;](AplNet.Simd.FusedMultiplyAddOperator-1.md) | struct | `x * multiplier + addend` as a fused multiply-add: one rounding instead of the two of [`MultiplyAddOperator<T>`](AplNet.Simd.MultiplyAddOperator-1.md). The explicit opt-in, for `float` and `double`. Results match `Math.FusedMultiplyAdd` bit for bit in every lane, so they can differ from [`MultiplyAddOperator<T>`](AplNet.Simd.MultiplyAddOperator-1.md) in the last bit. On hardware without FMA instructions the runtime emulates it exactly, which is slow. |
| [IBinaryOperator&lt;T&gt;](AplNet.Simd.IBinaryOperator-1.md) | interface | An elementwise function `f(x, y)`, stated for scalars and for each vector width. |
| [IReduceOperator&lt;T&gt;](AplNet.Simd.IReduceOperator-1.md) | interface | A monoid for reductions: an associative (and, for parallel use, commutative) binary operation with an identity, plus the horizontal reduction of one vector to a scalar. Sum, min and max are built in; implement this for a custom one. |
| [IUnaryOperator&lt;T&gt;](AplNet.Simd.IUnaryOperator-1.md) | interface | An elementwise function `f(x)`, stated for scalars and for each vector width. |
| [IdentityOperator&lt;T&gt;](AplNet.Simd.IdentityOperator-1.md) | struct | `x`. Used as the "map" of a plain reduction; the JIT inlines it away. |
| [LogOperator&lt;T&gt;](AplNet.Simd.LogOperator-1.md) | struct | The natural logarithm `ln(x)`, for `float` and `double`. Like [`ExpOperator<T>`](AplNet.Simd.ExpOperator-1.md), the vector form is within a few ulp of the scalar one rather than bit-identical to it. |
| [MaxOperator&lt;T&gt;](AplNet.Simd.MaxOperator-1.md) | struct | `max(x, y)`; as a reduction, the maximum (identity: -infinity, or the type's minimum). |
| [MinOperator&lt;T&gt;](AplNet.Simd.MinOperator-1.md) | struct | `min(x, y)`; as a reduction, the minimum (identity: +infinity, or the type's maximum). |
| [MultiplyAddOperator&lt;T&gt;](AplNet.Simd.MultiplyAddOperator-1.md) | struct | `x * multiplier + addend`: the AXPY-style affine map. Computed as a multiply then an add, so results match the scalar expression bit for bit (a fused multiply-add would round once instead of twice). |
| [MultiplyOperator&lt;T&gt;](AplNet.Simd.MultiplyOperator-1.md) | struct | `x * y`; as a reduction, the product (identity 1). |
| [NegateOperator&lt;T&gt;](AplNet.Simd.NegateOperator-1.md) | struct | `-x`. |
| [ScaleOperator&lt;T&gt;](AplNet.Simd.ScaleOperator-1.md) | struct | `x * factor`. |
| [SimdCapabilities](AplNet.Simd.SimdCapabilities.md) | class | What the SIMD kernels can use on this machine, as the runtime reports it. |
| [SimdOps](AplNet.Simd.SimdOps.md) | class | SIMD bulk operations - map, map-reduce and in-place transform - single-threaded over spans and parallel over arrays or `Memory<T>`. |
| [SqrtOperator&lt;T&gt;](AplNet.Simd.SqrtOperator-1.md) | struct | `sqrt(x)`, for `float` and `double`. |
| [SquareOperator&lt;T&gt;](AplNet.Simd.SquareOperator-1.md) | struct | `x * x`. |
| [SubtractOperator&lt;T&gt;](AplNet.Simd.SubtractOperator-1.md) | struct | `x - y`. |

## AplNet.Unsafe

| Type | Kind | Summary |
|---|---|---|
| [NativeBuffer&lt;T&gt;](AplNet.Unsafe.NativeBuffer-1.md) | class | A fixed-size buffer of `T` in native memory, aligned to 64 bytes (one cache line, and one AVX-512 vector). Never moved by the GC, so its [`NativeBuffer<T>.Pointer`](AplNet.Unsafe.NativeBuffer-1.md) can be handed to [`UnsafeParallel`](AplNet.Unsafe.UnsafeParallel.md) without pinning, and it adds nothing to the managed heap. |
| [UnsafeParallel](AplNet.Unsafe.UnsafeParallel.md) | class | **UNSAFE.** Parallel loops over raw pointers with function-pointer bodies: no delegates, no bounds checks, no range validation. The "you asked for it" tier. |
