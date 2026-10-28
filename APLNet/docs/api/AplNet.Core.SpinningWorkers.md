# SpinningWorkers class

Namespace: `AplNet.Core` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public sealed class SpinningWorkers : IDisposable
```

An opt-in set of dedicated worker threads that keep spinning between loops instead of returning to the thread pool. Pass it in [`AplOptions.Workers`](AplNet.AplOptions.md).

**Remarks**

Waking pool threads costs a few microseconds per loop. That is noise for a loop that runs for milliseconds, but it dominates very small loops that run very often, such as the per-tick update of a game or a simulation. These workers busy-wait for [`SpinningWorkers.SpinDuration`](AplNet.Core.SpinningWorkers.md) after each loop, so the next loop finds them awake. That trades CPU time (a spinning worker keeps a core busy) for latency. After the spin duration with nothing to do they block, and cost nothing until the next loop wakes them.

Placement is stable: on every loop, worker `k` first tries to claim the partition after the caller's, the `(k + 1)`-th, and only then helps with the others. Combined with [`WorkerAffinity`](AplNet.Core.WorkerAffinity.md), the same part of the data is processed by the same core (or NUMA node) call after call. Placement applies to the static and striped partitioners; work stealing moves work between workers by design.

Safe to share between threads and loops. Each worker has a one-item mailbox. A loop that finds it full (another caller's loop has not been picked up yet) queues that share to the thread pool instead. A job left waiting while its worker is busy, for example in a nested loop, simply runs late, and by then the caller has usually done its share. A busy group can slow a loop down, but it never blocks it. Dispose the group to stop its threads; loops that still reference it afterwards fall back to the thread pool.

## Constructors

### SpinningWorkers(int, TimeSpan?, WorkerAffinity)

```csharp
public SpinningWorkers(int threadCount = -1, TimeSpan? spinDuration = null, WorkerAffinity affinity = WorkerAffinity.None);
```

Starts `threadCount` spinning worker threads.

**Parameters**

- `threadCount`: The number of threads, not counting the callers that will use them. -1 means `Environment.ProcessorCount - 1`, which serves a loop at the default degree of parallelism.
- `spinDuration`: How long a worker keeps spinning after its last loop before it blocks. `null` means 1 ms; `Timeout.InfiniteTimeSpan` spins forever.
- `affinity`: Where to pin the threads. Pinning is applied on Windows and Linux; elsewhere it is ignored, and [`SpinningWorkers.IsAffinityApplied`](AplNet.Core.SpinningWorkers.md) says so.

## Properties

### Affinity

```csharp
public WorkerAffinity Affinity { get; }
```

The pinning that was requested.

### IsAffinityApplied

```csharp
public bool IsAffinityApplied { get; }
```

Every thread was pinned as [`SpinningWorkers.Affinity`](AplNet.Core.SpinningWorkers.md) asked. False when no pinning was asked for, or the platform does not support it.

### SpinDuration

```csharp
public TimeSpan SpinDuration { get; }
```

How long a worker spins after its last loop before it blocks.

### ThreadCount

```csharp
public int ThreadCount { get; }
```

The number of worker threads.

## Methods

### Dispose()

```csharp
public void Dispose();
```

Stops the threads after they finish any loop they are part of. Idempotent.
