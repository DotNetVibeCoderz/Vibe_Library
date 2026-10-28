# ILongRangeWorkBody interface

Namespace: `AplNet.Core` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public interface ILongRangeWorkBody
```

A loop body invoked once per contiguous sub-range of a 64-bit range: the `long` counterpart of [`IRangeWorkBody`](AplNet.Core.IRangeWorkBody.md).

## Methods

### Invoke(long, long)

```csharp
public void Invoke(long fromInclusive, long toExclusive);
```

Runs every iteration in `[fromInclusive, toExclusive)`.
