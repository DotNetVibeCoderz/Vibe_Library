# Diagnostics

[Bahasa Indonesia](../id/diagnostics.md) · [Index](README.md)

`AplNet.Diagnostics.AplEventSource` publishes EventCounters under the name **`AplNet`**:

| Counter | Meaning |
|---|---|
| `loops-executed` | Loops that ran on more than one worker |
| `inline-loops-executed` | Loops that were too small to split and ran on the caller |
| `chunks-executed` | Ranges handed to a body: partitions, stripes, blocks or grains |
| `steals` | Successful steals by `WorkStealingPartitioner` |

The counters are **off by default and free while off.** Every increment sits behind
`EventSource.IsEnabled()`, which is a single field read, and the counters themselves are only
created once a listener enables the source.

```sh
dotnet-counters monitor --counters AplNet -p <pid>
```

In-process, read the totals directly while a listener is attached:

```csharp
long steals = AplEventSource.Log.StealCount;
long chunks = AplEventSource.Log.ChunksExecuted;
```

**What to look for:**

- **Few chunks per loop while the lanes are uneven:** try striping or work stealing.
- **Very many chunks with cheap iterations:** raise `CancellationCheckInterval`, or the
  work-stealing grain.
- **Many inline loops:** your loops are below `MinChunkSize`. That is usually right for tiny
  loops.

Under NativeAOT, EventSource support is trimmed away unless the app sets
`<EventSourceSupport>true</EventSourceSupport>`. APL.Net works the same either way; it just stops
counting.
