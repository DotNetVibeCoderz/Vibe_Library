# IReduceBody&lt;T&gt; interface

Namespace: `AplNet.Core` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public interface IReduceBody<T>
```

A parallel reduction: each worker folds its ranges into a private accumulator, and the accumulators are then combined.

**Type parameters**

- `T`: The accumulator type.

**Remarks**

[`IReduceBody<T>.Combine`](AplNet.Core.IReduceBody-1.md) must be associative, and the identity passed to the reduce call must be a true identity for it (`Combine(identity, x) == x`): partitions are combined in a fixed order, but how the range is split depends on the degree of parallelism. Floating-point sums therefore match a sequential sum only up to rounding.

## Methods

### Accumulate(int, int, T)

```csharp
public T Accumulate(int fromInclusive, int toExclusive, T accumulator);
```

Folds every iteration in `[fromInclusive, toExclusive)` into `accumulator`.

### Combine(T, T)

```csharp
public T Combine(T left, T right);
```

Combines two partial results.
