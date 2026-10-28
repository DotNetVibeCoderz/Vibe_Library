# StaticRangePartitioner class

Namespace: `AplNet.Core.Partitioners` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public sealed class StaticRangePartitioner : IPartitioner
```

The default: one contiguous range per worker, all the same size to within one item.

**Remarks**

Computed once up front, with no interlocked chunk claiming while the loop runs - which is what removes the per-chunk overhead of TPL's dynamic partitioner. The trade-off is load balance: if some iterations cost far more than others, the worker that owns them finishes last and the rest wait. Use [`WorkStealingPartitioner`](AplNet.Core.Partitioners.WorkStealingPartitioner.md) for such loops; do not make this one dynamic.

Sizes are balanced (the first `length % count` partitions get one extra item) rather than `ceil(length / count)` each: with 9 items and 8 workers the ceiling split would give five workers two items and leave three idle, while this gives one worker two and the rest one each.

**Straggler tolerance (opt-in).** With one partition per worker, a single worker that the OS deschedules - another process, a busy core - holds up the whole loop. Passing `partitionsPerWorker` greater than 1 splits the range into that many partitions per worker. They are still fixed before the loop starts and claimed whole, one interlocked increment per partition, so a late worker finds the leftovers already taken and the others absorb its share. The price is `k` times as many claims and body calls; 2 to 4 is usually enough.

## Constructors

### StaticRangePartitioner(int)

```csharp
public StaticRangePartitioner(int partitionsPerWorker = 1);
```

Creates a static partitioner that splits each loop into `partitionsPerWorker` equal partitions per worker. 1, the default, is the classic one-range-per-worker split.

## Properties

### Instance

```csharp
public static StaticRangePartitioner Instance { get; }
```

The shared instance: one partition per worker.

### PartitionsPerWorker

```csharp
public int PartitionsPerWorker { get; }
```

How many partitions each worker's share is split into.

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
