# IRangeWorkBody interface

Namespace: `AplNet.Core` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public interface IRangeWorkBody
```

A loop body invoked once per contiguous sub-range rather than once per index.

**Remarks**

The fastest shape APL.Net offers. Inside [`IRangeWorkBody.Invoke`](AplNet.Core.IRangeWorkBody.md) the body can slice arrays into spans (the JIT then removes the bounds checks from a `for` loop over the span), hoist invariants out of the loop, or hand the whole slice to a SIMD kernel. Ranges never overlap and together cover the loop exactly once.

## Methods

### Invoke(int, int)

```csharp
public void Invoke(int fromInclusive, int toExclusive);
```

Runs every iteration in `[fromInclusive, toExclusive)`.
