# AplLoopResult struct

Namespace: `AplNet.Core` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public readonly struct AplLoopResult
```

How a stateful loop ended: the equivalent of `ParallelLoopResult`.

## Properties

### IsCompleted

```csharp
public bool IsCompleted { get; }
```

Every iteration ran: neither [`AplLoopState.Stop`](AplNet.Core.AplLoopState.md) nor [`AplLoopState.Break`](AplNet.Core.AplLoopState.md) was called.

### LowestBreakIteration

```csharp
public int? LowestBreakIteration { get; }
```

The lowest index [`AplLoopState.Break`](AplNet.Core.AplLoopState.md) was called from, or `null` if it was not called.
