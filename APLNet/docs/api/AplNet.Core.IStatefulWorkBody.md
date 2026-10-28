# IStatefulWorkBody interface

Namespace: `AplNet.Core` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public interface IStatefulWorkBody
```

A loop body that can end its loop early through [`AplLoopState`](AplNet.Core.AplLoopState.md): the struct form of `Parallel.For` with a `ParallelLoopState`.

## Methods

### Invoke(int, AplLoopState)

```csharp
public void Invoke(int index, AplLoopState state);
```

Runs one iteration. `state` is valid only during this call.
