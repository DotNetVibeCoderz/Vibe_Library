# FixedParallelismProvider class

Namespace: `AplNet.Core` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public sealed class FixedParallelismProvider : IParallelismProvider
```

Always reports the same degree of parallelism.

## Constructors

### FixedParallelismProvider(int)

```csharp
public FixedParallelismProvider(int degreeOfParallelism);
```

Creates a provider that always reports `degreeOfParallelism`.

## Properties

### DegreeOfParallelism

```csharp
public int DegreeOfParallelism { get; }
```

The number of workers a loop should use when none is specified.
