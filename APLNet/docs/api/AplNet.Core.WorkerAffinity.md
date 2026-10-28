# WorkerAffinity enum

Namespace: `AplNet.Core` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public enum WorkerAffinity
```

Where [`SpinningWorkers`](AplNet.Core.SpinningWorkers.md) pins its threads.

## Values

| Name | Value | Description |
|---|---|---|
| `None` | 0 | No pinning: the OS schedules the threads wherever it likes. The default. |
| `Core` | 1 | Each worker is pinned to one logical processor, in processor order. Worker `k` runs on the processor after worker `k - 1`'s, so with stable partition placement the same slice of an array is processed by the same core on every call and stays in that core's cache. |
| `NumaNode` | 2 | Each worker is pinned to the NUMA node of the processor [`WorkerAffinity.Core`](AplNet.Core.WorkerAffinity.md) would have chosen, and may run on any processor of that node. Contiguous partitions go to workers on the same node, so the memory a node first touches is the memory it keeps working on. On a machine with one node this only keeps the threads off nothing; it is meant for multi-socket servers. |
