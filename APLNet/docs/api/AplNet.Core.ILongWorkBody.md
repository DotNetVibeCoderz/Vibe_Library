# ILongWorkBody interface

Namespace: `AplNet.Core` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public interface ILongWorkBody
```

A loop body invoked once per index of a 64-bit range: the `long` counterpart of [`IWorkBody`](AplNet.Core.IWorkBody.md), for loops over more than `Int32.MaxValue` items.

## Methods

### Invoke(long)

```csharp
public void Invoke(long index);
```

Runs one iteration.
