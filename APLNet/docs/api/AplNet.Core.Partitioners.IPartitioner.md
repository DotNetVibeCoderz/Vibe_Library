# IPartitioner interface

Namespace: `AplNet.Core.Partitioners` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public interface IPartitioner
```

Splits the index range `[0, length)` of a loop into partitions, once, before any work starts.

**Remarks**

A partition is a sequence of one or more non-overlapping sub-ranges. Workers claim whole partitions (one interlocked increment per partition, never per iteration or per chunk) and run every sub-range of the partition they claimed. Across all partitions the sub-ranges must cover `[0, length)` exactly once.

Implementations must be thread-safe and deterministic: the executor may ask for the same range more than once and from any thread. Both methods are called once per partition or sub-range, never per iteration, so they need not be fast - but they must not allocate if the caller relies on the zero-allocation guarantee.

## Methods

### GetPartitionCount(int, int)

```csharp
public int GetPartitionCount(int length, int maxWorkers);
```

The number of partitions to create for `length` items when at most `maxWorkers` workers are available. Must be at least 1 when `length` is positive.

### TryGetRange(int, int, int, int, int, int)

```csharp
public bool TryGetRange(int length, int partitionCount, int partition, int index, out int fromInclusive, out int toExclusive);
```

Gets the `index`-th sub-range of `partition`, relative to the start of the loop. Returns `false` once the partition has no more sub-ranges.
