# AplRangeBodyAttribute class

Namespace: `AplNet` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public sealed class AplRangeBodyAttribute : Attribute
```

Like [`AplBodyAttribute`](AplNet.AplBodyAttribute.md), for a method whose first two parameters are a range (`int fromInclusive, int toExclusive`). The generated struct implements [`IRangeWorkBody`](AplNet.Core.IRangeWorkBody.md) and is used with [`Apl.ForRange<T>`](AplNet.Apl.md).

## Constructors

### AplRangeBodyAttribute()

```csharp
public AplRangeBodyAttribute();
```
