# AplTopology class

Namespace: `AplNet.Core` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public static class AplTopology
```

The machine's NUMA nodes and logical processors, as [`SpinningWorkers`](AplNet.Core.SpinningWorkers.md) sees them when it places its threads.

**Remarks**

Read once, on first use. On Windows the topology comes from the NUMA API (processors are numbered `group * 64 + index`); on Linux from `/sys/devices/system/node`, limited to the processors this process may run on. Elsewhere, or when the information is unavailable, the machine is reported as one node holding processors `0 .. ProcessorCount - 1`. This is the only place APL.Net calls native code, and only when thread affinity is asked for.

## Properties

### IsAffinitySupported

```csharp
public static bool IsAffinitySupported { get; }
```

Whether this platform supports pinning threads (Windows and Linux).

### NumaNodeCount

```csharp
public static int NumaNodeCount { get; }
```

The number of NUMA nodes. 1 on most desktops and laptops.

## Methods

### Describe()

```csharp
public static string Describe();
```

A one-line summary, e.g. `"2 NUMA nodes: [0-15] [16-31]"`.

### GetNodeProcessors(int)

```csharp
public static IReadOnlyList<int> GetNodeProcessors(int node);
```

The logical processors of NUMA node `node`, in ascending order.

**Exceptions**

- `ArgumentOutOfRangeException`: `node` is not a node index.
