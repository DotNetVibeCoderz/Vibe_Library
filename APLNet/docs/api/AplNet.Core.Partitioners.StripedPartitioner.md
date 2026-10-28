# StripedPartitioner class

Namespace: `AplNet.Core.Partitioners` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public sealed class StripedPartitioner : IPartitioner
```

Deals fixed-size stripes round-robin: worker `w` of `W` runs stripes `w, w + W, w + 2W, ...`.

**Remarks**

Still static - every assignment is known before the loop starts - but interleaved. That helps when cost varies smoothly with the index (a triangular matrix, an image whose top half is empty): each worker gets a slice of every region instead of one worker getting all of the expensive one. It also keeps all workers moving through the same neighbourhood of memory at the same time, which suits access patterns where nearby iterations share data.

Pick a stripe large enough to amortise the per-stripe call (thousands of cheap iterations, or a handful of expensive ones) and, for arrays, a multiple of 16 so stripes start on cache-line boundaries for 4-byte elements.

## Constructors

### StripedPartitioner(int)

```csharp
public StripedPartitioner(int stripeSize = 4096);
```

Creates a partitioner dealing stripes of `stripeSize` iterations.

## Properties

### StripeSize

```csharp
public int StripeSize { get; }
```

The number of consecutive iterations in one stripe.

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
