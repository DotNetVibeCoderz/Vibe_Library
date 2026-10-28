# Spinning workers

[Bahasa Indonesia](../id/workers.md) · [Index](README.md)

By default every APL.Net loop runs on the .NET thread pool. Queuing work there is cheap, but the
pool's threads go to sleep when they have nothing to do, and waking them costs a few
microseconds per loop. A loop that runs for milliseconds never notices. A loop that runs for a few
microseconds, thousands of times a second (a game or simulation tick, an iterative solver, an
audio block), pays it on every call.

`SpinningWorkers` is the opt-in answer: a set of dedicated threads that keep spinning for a
while after each loop, so the next loop finds them awake.

```csharp
using AplNet;
using AplNet.Core;

using var workers = new SpinningWorkers();               // ProcessorCount - 1 threads
var options = new AplOptions { Workers = workers };

while (running)
{
    Apl.For(0, bodies.Length, new Integrate(bodies, dt), options);   // no thread is woken
    Apl.For(0, bodies.Length, new Collide(bodies), options);
}
```

## The trade-off

| | Thread pool (default) | `SpinningWorkers` |
|---|---|---|
| Cost to start a loop | Wake pool threads (µs) | Write one slot per worker (ns) |
| CPU between loops | None | Each worker spins for `SpinDuration`, then sleeps |
| Shared with the rest of the app | Yes | No: dedicated threads |
| ExecutionContext | Not flowed | Not flowed |

A spinning worker keeps a core busy. For a loop that runs every few microseconds that is what you
want. For a loop that runs once a second it wastes power. `SpinDuration` (default 1 ms) bounds the
waste: after that long with no work, a worker blocks and costs nothing until the next loop wakes
it. `Timeout.InfiniteTimeSpan` never blocks.

Measured gains are in [benchmark results, B8](benchmark-results.md). They are modest on top of an
already cheap pool path, and largest for loops of a few thousand to tens of thousands of elements.

## Constructor

```csharp
new SpinningWorkers(
    threadCount: -1,                  // -1: Environment.ProcessorCount - 1
    spinDuration: null,               // null: 1 ms; Timeout.InfiniteTimeSpan: spin forever
    affinity: WorkerAffinity.None)    // None, Core or NumaNode
```

The caller always works too, so `ProcessorCount - 1` threads serve a loop at the default degree of
parallelism. Dispose the group to stop its threads. A loop that still references a disposed group
runs on the thread pool instead.

## Stable placement

On every loop, worker *k* first tries to claim partition *k + 1* (the caller's is partition 0), and
only then helps with the others. With the static and striped partitioners the same slice of your
data is therefore processed by the same thread call after call, and stays warm in that core's
cache. Work stealing moves work between workers by design, so it keeps its own claiming.

Placement is best effort, never a wait: if worker *k* is late, the caller or another worker takes
its partition, exactly as on the thread pool. A loop never blocks on a particular worker.

## Pinning: `WorkerAffinity`

| Value | Effect |
|---|---|
| `None` (default) | The OS schedules the threads freely |
| `Core` | Worker *k* is pinned to one logical processor, in processor order, starting after the first |
| `NumaNode` | Worker *k* is pinned to the NUMA node of that processor, and may use any processor in the node |

Pinning combines with stable placement. With `Core`, partition *k + 1* runs on the same core every
time. With `NumaNode`, contiguous partitions go to workers on the same node, so the memory a node
first touches (Windows and Linux both place a page on the node that first writes it) is the memory
it keeps working on. That is what NUMA-aware placement means here. Allocate and initialise large
arrays with the same loop shape you later compute with, so the first touch already happens on the
right node.

`IsAffinityApplied` reports whether every thread was pinned. Pinning works on Windows (through
processor groups, so beyond 64 processors too) and Linux (`sched_setaffinity`, respecting the
process's allowed CPU set). On macOS there is no thread-affinity API. The workers run unpinned there,
and `IsAffinityApplied` is false.

## Topology

```csharp
Console.WriteLine(AplTopology.Describe());        // "2 NUMA nodes: [0-15] [16-31]"
int nodes = AplTopology.NumaNodeCount;
IReadOnlyList<int> cpus = AplTopology.GetNodeProcessors(0);
```

Read once, on first use: from the NUMA API on Windows and from `/sys/devices/system/node` on Linux.
Elsewhere, or if that information is unavailable, the machine is one node holding processors
`0 .. ProcessorCount - 1`. These calls and the pinning are the only native interop in APL.Net, and
they run only when affinity is requested ([design decisions §12](design-decisions.md)).

## What has been verified

- Correctness with every partitioner, reductions, nested loops, concurrent callers, failures,
  cancellation, and a worker that sleeps between every loop (`SpinningWorkersTests`).
- Zero allocation per call (`AllocationTests`).
- Stable placement: on an otherwise idle machine, partitions keep their worker on at least 75% of
  calls (`SpinningWorkerPlacementTests`). In practice it is nearly all of them.
- Pinning applied on Windows and Linux, including Arm64 Linux in CI, and under NativeAOT.
- **Not verified:** a speed-up from NUMA placement. That needs a multi-socket machine, and the
  reference machine has one node. The mapping logic is unit-tested with a synthetic two-node
  topology.
