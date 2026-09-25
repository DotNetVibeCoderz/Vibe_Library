# API guide

[Bahasa Indonesia](../id/api-guide.md) · [Index](README.md)

## `Apl` (namespace `AplNet`)

| Method | Body | Notes |
|---|---|---|
| `For(from, to, Action<int>)` | delegate | Drop-in for `Parallel.For` |
| `For<TBody>(from, to, TBody)` | `struct, IWorkBody` | Zero-allocation; `Invoke` is inlined |
| `ForRange(from, to, Action<int,int>)` | delegate | One call per slice |
| `ForRange<TBody>(from, to, TBody)` | `struct, IRangeWorkBody` | The fastest shape |
| `ForEach<T>(T[], Action<T>)` | delegate | By value, like `Parallel.ForEach` |
| `ForEach<T>(T[], RefItemAction<T>)` | delegate | `(int index, ref T item)`: update elements in place |
| `ForEach<T,TBody>(T[] or Memory<T>, TBody)` | `struct, IWorkBody<T>` | By `ref`, looped over a span |
| `Reduce<T>(from, to, identity, accumulate, combine)` | delegates | `accumulate(from, to, acc)` folds one slice |
| `Reduce<T,TBody>(from, to, identity, TBody)` | `struct, IReduceBody<T>` | Zero-allocation reduction |
| `ForAsync`, `ForEachAsync` (`IEnumerable`, `IAsyncEnumerable`) | async delegate | Thin wrappers over TPL; *do* flow ExecutionContext |

Every method takes an optional `AplOptions`. `ParallelExecutor` (in `AplNet.Core`) exposes the
struct-only forms (`For`, `ForRange`, `Reduce`) for callers who prefer the spec's name.

## Body interfaces (namespace `AplNet.Core`)

```csharp
public interface IWorkBody        { void Invoke(int index); }
public interface IWorkBody<T>     { void Invoke(int index, ref T item); }
public interface IRangeWorkBody   { void Invoke(int fromInclusive, int toExclusive); }
public interface IReduceBody<T>
{
    T Accumulate(int fromInclusive, int toExclusive, T accumulator);
    T Combine(T left, T right);
}
```

Guidelines:

- Implement bodies as `readonly struct` and pass them by their concrete type. Passing one
  through an interface-typed variable boxes it and loses the specialisation.
- Each worker invokes **its own copy** of the struct. Reference fields (arrays, objects) are
  shared between copies; value fields are not.
- A range body receives ranges that never overlap and together cover the loop exactly once.
  When the token can be canceled, each range is at most `CancellationCheckInterval` long.
  Otherwise a range body receives whole partitions.
- For `Reduce`, `Combine` must be associative, and `identity` must be its identity. Partitions
  are combined in a fixed order, so floating-point results are reproducible for a fixed degree of
  parallelism and partitioner.

## `AplOptions`

| Property | Default | Meaning |
|---|---|---|
| `MaxDegreeOfParallelism` | `null` → provider (ProcessorCount) | Workers, counting the caller. `-1` also means default |
| `CancellationToken` | none | Checked before the start and between blocks |
| `CancellationCheckInterval` | 4096 | Block length when the token can cancel |
| `MinChunkSize` | loops: 1, SIMD: 32,768 | At most `ceil(n / MinChunkSize)` workers |
| `Partitioner` | `StaticRangePartitioner.Instance` | See [partitioners](partitioners.md) |
| `ParallelismProvider` | `DefaultParallelismProvider` | Inject `FixedParallelismProvider(n)` in tests |

`AplOptions.Default` is shared. Options are immutable once built, so create them once and reuse
them.

## Exceptions and cancellation (TPL-compatible)

- Every exception thrown by a body is collected. Once all running workers have stopped, they are
  rethrown together as **one `AggregateException`**. A failure makes the other workers stop at
  their next partition or block boundary. A loop small enough to run inline still wraps its
  exception, as TPL does.
- A token already canceled when the loop starts throws `OperationCanceledException` before any
  iteration runs.
- Canceling mid-run stops workers at their next block boundary, and the loop throws
  `OperationCanceledException(token)`. A body that throws an `OperationCanceledException` carrying
  the loop's own token is treated as cancellation, not failure. Any other
  `OperationCanceledException` is a failure.
- A range longer than `Int32.MaxValue` iterations throws `ArgumentOutOfRangeException`. An empty
  or reversed range does nothing, and `Reduce` returns the identity.

## Parallelism providers

```csharp
public interface IParallelismProvider { int DegreeOfParallelism { get; } }
DefaultParallelismProvider.Instance      // Environment.ProcessorCount
new FixedParallelismProvider(3)          // deterministic, for tests
```
