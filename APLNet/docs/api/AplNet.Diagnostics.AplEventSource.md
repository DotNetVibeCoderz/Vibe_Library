# AplEventSource class

Namespace: `AplNet.Diagnostics` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public sealed class AplEventSource : EventSource
```

Lightweight counters for APL.Net, published as EventCounters under the name `AplNet`.

**Remarks**

Off by default and free while off: every increment is behind `EventSource.IsEnabled`, a single field read, and the counters themselves are only created when a listener enables the source. Watch them with `dotnet-counters monitor --counters AplNet -p <pid>` or an in-process `EventListener`.

Under NativeAOT, EventSource support is trimmed away unless the app sets `<EventSourceSupport>true</EventSourceSupport>`; the library works the same either way, it just stops counting.

## Fields

### Log

```csharp
public static readonly AplEventSource Log;
```

The single instance.

## Properties

### ChunksExecuted

```csharp
public long ChunksExecuted { get; }
```

Ranges handed to a loop body: partitions, stripes, blocks or work-stealing grains.

### InlineLoopsExecuted

```csharp
public long InlineLoopsExecuted { get; }
```

Loops that ran entirely on the calling thread because they were too small to split.

### LoopsExecuted

```csharp
public long LoopsExecuted { get; }
```

Loops that ran on more than one worker since the process started (while enabled).

### StealCount

```csharp
public long StealCount { get; }
```

Successful steals by [`WorkStealingPartitioner`](AplNet.Core.Partitioners.WorkStealingPartitioner.md).
