# WorkStealingPartitioner class

Namespace: `AplNet.Core.Partitioners` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public sealed class WorkStealingPartitioner : IPartitioner
```

Opt-in dynamic load balancing for loops whose iterations vary widely in cost.

**Remarks**

Each worker starts with the same contiguous range [`StaticRangePartitioner`](AplNet.Core.Partitioners.StaticRangePartitioner.md) would give it and consumes it from the front in grains of [`WorkStealingPartitioner.MinGrainSize`](AplNet.Core.Partitioners.WorkStealingPartitioner.md) iterations. A worker that runs dry steals the back half of whatever remains of the fullest other range. Both ends of a range live in one 64-bit word updated by compare-and-swap, so the owner and any number of thieves agree on who runs what without locks. This is the index-range form of a Chase-Lev deque: the owner takes from one end and thieves from the other, and because the work is a contiguous range rather than a list of tasks, stealing half costs one CAS no matter how large the half is.

The price is one CAS per grain on the owner's side, which is why this is not the default: for uniform, cheap iterations it is pure overhead. Choose it for skewed workloads (Mandelbrot rows, Zipf-distributed item costs, sparse data), where it recovers most of what static partitioning loses. Grains are also the unit at which cancellation and failures stop a worker.

## Constructors

### WorkStealingPartitioner(int)

```csharp
public WorkStealingPartitioner(int minGrainSize = 0);
```

Creates a work-stealing partitioner. `minGrainSize` is the number of iterations a worker takes from its own range at a time; 0 chooses `max(1, length / (workers * 1024))`, which keeps the CAS cost under a tenth of a percent of the iterations while leaving ~1000 grains per worker for thieves to take.

## Properties

### Instance

```csharp
public static WorkStealingPartitioner Instance { get; }
```

The shared instance, with an automatic grain size.

### MinGrainSize

```csharp
public int MinGrainSize { get; }
```

Iterations taken per grain, or 0 for automatic.

## Methods

### GetGrainSize(int, int)

```csharp
public int GetGrainSize(int length, int workers);
```

The grain size used for a loop of `length` items over `workers` workers.

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
