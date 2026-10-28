# IWorkBody interface

Namespace: `AplNet.Core` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public interface IWorkBody
```

A loop body invoked once per index. Implement it on a `struct` (ideally a `readonly struct`) and pass it by its concrete type.

**Remarks**

This is the mechanism that replaces delegate dispatch. APL.Net's loops are generic over `TBody : struct, IWorkBody`; for a struct type argument the JIT compiles a dedicated copy of the loop, so the call to [`IWorkBody.Invoke`](AplNet.Core.IWorkBody.md) is a direct call it can inline - no delegate, no closure allocation, no interface dispatch.

Each worker invokes its own copy of the struct. Fields that are references (arrays, objects) are shared; plain value fields are not, so mutating them does not communicate between workers.

## Methods

### Invoke(int)

```csharp
public void Invoke(int index);
```

Runs one iteration.
