# Partitioners

[Bahasa Indonesia](../id/partitioners.md) · [Index](README.md)

A partitioner decides, before any work starts, how `[0, n)` is divided. You choose it per call
with `AplOptions.Partitioner`.

| Partitioner | How work is split | Cost at run time | Best for |
|---|---|---|---|
| `StaticRangePartitioner` (default) | One contiguous slice per worker, sizes within 1 | 1 increment per partition | Uniform iterations (most numeric loops) |
| `StripedPartitioner(stripe)` | Fixed stripes dealt round-robin: worker *w* gets stripes *w*, *w+W*, … | 1 increment per partition | Cost that varies smoothly with the index; locality |
| `WorkStealingPartitioner(grain)` | Static start; owners take grains from the front, idle workers steal the back half of the fullest range | 1 CAS per grain, 1 CAS per steal | Skewed or unpredictable costs |
| Your own `IPartitioner` | Any fixed plan | 1 increment per partition | Special layouts |

![Lanes: static vs striped vs work stealing](../images/gallery-mandelbrot-full.png)

## Static

```csharp
Apl.For(0, n, body);   // StaticRangePartitioner.Instance
```

This is the cheapest option and the right default. Its weakness is load balance: the worker
that owns the expensive part of the range finishes last. See
[design decisions §1](design-decisions.md#1-static-partitioning-is-the-default).

## Striped

```csharp
var options = new AplOptions { Partitioner = new StripedPartitioner(stripeSize: 64) };
```

Every worker gets a slice of every region of the range. When cost depends on position (the
middle rows of a Mandelbrot image, the long rows of a triangular matrix), each worker then gets
its fair share of the expensive region. There is still no shared counter while the loop runs.
Choose a stripe large enough to amortise the call per stripe. For arrays of 4-byte elements, a
multiple of 16 keeps stripes aligned to cache lines.

## Work stealing

```csharp
var options = new AplOptions { Partitioner = new WorkStealingPartitioner(minGrainSize: 1) };
```

This is the index-range form of a Chase-Lev deque. Each worker's remaining range
`[start, end)` lives in one 64-bit word, padded to its own cache line:

- The **owner** takes `grain` iterations from the front with one CAS.
- A worker that runs dry becomes a **thief**. It scans for the fullest range and CAS-es it down
  to its front half, then moves the back half into its own slot. Stealing half the remaining
  work costs one CAS, however much work that is.
- On failure or cancellation, the workers drain every slot, counting the abandoned iterations
  so the caller stops waiting.

`minGrainSize: 0` (the default for `WorkStealingPartitioner.Instance`) chooses
`max(1, n / (workers * 1024))`. Use `1` when single iterations are expensive (rows,
documents, simulations). Use a large grain when iterations are cheap: every grain costs a CAS.

## Writing your own

```csharp
public sealed class EveryOtherBlock : IPartitioner
{
    public int GetPartitionCount(int length, int maxWorkers) => Math.Min(length, maxWorkers * 4);

    public bool TryGetRange(int length, int partitionCount, int partition, int index,
                            out int from, out int to) { /* the index-th sub-range of partition */ }
}
```

Rules:

- The sub-ranges of all partitions must cover `[0, length)` exactly once.
- The methods must be deterministic and thread-safe.
- Returning more partitions than workers is allowed. Workers claim them one at a time, which is
  a coarse form of dynamic balancing that you have opted into.
- Returning 0 partitions throws `InvalidOperationException`.

## Choosing, in practice

1. Start with the default.
2. If the Gallery's lane chart (or a profiler) shows some lanes ending long before the others,
   try `StripedPartitioner` first: it adds no cost while the loop runs.
3. If costs are skewed rather than positional (Zipf-like, data-dependent), use
   `WorkStealingPartitioner(1)`.

Measured on the reference machine ([B6](benchmark-results.md)): with the most expensive items
first, static partitioning loses to `Parallel.For`, and work stealing beats it.
