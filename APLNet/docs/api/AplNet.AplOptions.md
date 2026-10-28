# AplOptions class

Namespace: `AplNet` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public sealed class AplOptions
```

Per-call settings for APL.Net loops: the equivalent of `ParallelOptions`.

**Remarks**

An instance is immutable once built and safe to share between threads and calls, so hot paths should create one up front and reuse it rather than allocating a new one per call.

## Constructors

### AplOptions()

```csharp
public AplOptions();
```

## Properties

### CancellationCheckInterval

```csharp
public int CancellationCheckInterval { get; init; }
```

When [`AplOptions.CancellationToken`](AplNet.AplOptions.md) can be canceled, the loop body is invoked in blocks of at most this many iterations and the token is checked between blocks. A range body therefore sees ranges no longer than this. Ignored when the token cannot be canceled: the body then receives whole partitions and pays nothing for cancellation support. Default 4096.

### CancellationToken

```csharp
public CancellationToken CancellationToken { get; init; }
```

Observed before the loop starts and between blocks of [`AplOptions.CancellationCheckInterval`](AplNet.AplOptions.md) iterations. A canceled loop throws `OperationCanceledException`, as TPL does.

### Default

```csharp
public static AplOptions Default { get; }
```

The options used when `null` is passed: every setting at its default.

### MaxDegreeOfParallelism

```csharp
public int? MaxDegreeOfParallelism { get; init; }
```

The most workers a loop may use, counting the calling thread. `null` or `-1` means "use [`AplOptions.ParallelismProvider`](AplNet.AplOptions.md)", which defaults to `Environment.ProcessorCount`.

**Exceptions**

- `ArgumentOutOfRangeException`: The value is zero or less than -1.

### MinChunkSize

```csharp
public int? MinChunkSize { get; init; }
```

The fewest iterations worth handing to one worker. A loop over `n` items uses at most `ceil(n / MinChunkSize)` workers, so tiny loops are not split into slices smaller than the cost of waking a thread. `null` uses the API's own default: 1 for the loop methods (the body's cost is unknown), 32 768 elements for the SIMD kernels.

### ParallelismProvider

```csharp
public IParallelismProvider? ParallelismProvider { get; init; }
```

Supplies the default degree of parallelism. Inject a fixed provider to make scheduling deterministic in tests, independent of the machine's core count.

### Partitioner

```csharp
public IPartitioner? Partitioner { get; init; }
```

How the index range is split into partitions. `null` means [`StaticRangePartitioner.Instance`](AplNet.Core.Partitioners.StaticRangePartitioner.md). See [`StripedPartitioner`](AplNet.Core.Partitioners.StripedPartitioner.md) and [`WorkStealingPartitioner`](AplNet.Core.Partitioners.WorkStealingPartitioner.md) for the alternatives.

### Workers

```csharp
public SpinningWorkers? Workers { get; init; }
```

Dedicated spinning worker threads to run the loop on instead of the thread pool. `null` (the default) uses the pool. Opt-in for very small loops that run very often; see [`SpinningWorkers`](AplNet.Core.SpinningWorkers.md) for the trade-off.

## Methods

### GetEffectiveDegreeOfParallelism()

```csharp
public int GetEffectiveDegreeOfParallelism();
```

The degree of parallelism this call will use before range-size limits are applied.
