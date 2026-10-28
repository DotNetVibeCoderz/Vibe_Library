# AplLoopState struct

Namespace: `AplNet.Core` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public readonly struct AplLoopState
```

Lets an iteration end its loop early, like `ParallelLoopState`. Passed by value to each iteration of a stateful loop; it is two fields wide and allocates nothing.

**Remarks**

[`AplLoopState.Stop`](AplNet.Core.AplLoopState.md) asks every worker to stop as soon as it can: no iteration starts after a worker has seen the request. [`AplLoopState.Break`](AplNet.Core.AplLoopState.md) promises only that the iterations *below* this one will still run; those above it may be skipped. Both are checked before every iteration, so an iteration that has already started always finishes.

As with TPL, a loop may use one or the other, not both: calling [`AplLoopState.Break`](AplNet.Core.AplLoopState.md) after [`AplLoopState.Stop`](AplNet.Core.AplLoopState.md) (or the reverse) throws `InvalidOperationException`. The value is valid only during the iteration it was passed to.

## Properties

### CurrentIteration

```csharp
public int CurrentIteration { get; }
```

The index of the iteration this state was passed to.

### IsStopped

```csharp
public bool IsStopped { get; }
```

[`AplLoopState.Stop`](AplNet.Core.AplLoopState.md) has been called by some iteration.

### LowestBreakIteration

```csharp
public int? LowestBreakIteration { get; }
```

The lowest index any iteration has called [`AplLoopState.Break`](AplNet.Core.AplLoopState.md) from, or `null`.

### ShouldExitCurrentIteration

```csharp
public bool ShouldExitCurrentIteration { get; }
```

The current iteration would not be started now: the loop was stopped, or broken at a lower index. Long-running iterations can poll it and return early.

## Methods

### Break()

```csharp
public void Break();
```

Ends the loop after every iteration below this one has run. Iterations above it that have not started are skipped.

**Exceptions**

- `InvalidOperationException`: [`AplLoopState.Stop`](AplNet.Core.AplLoopState.md) was already called.

### Stop()

```csharp
public void Stop();
```

Ends the loop as soon as possible. Iterations already running finish.

**Exceptions**

- `InvalidOperationException`: [`AplLoopState.Break`](AplNet.Core.AplLoopState.md) was already called.
