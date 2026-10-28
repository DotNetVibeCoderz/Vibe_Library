# DefaultParallelismProvider class

Namespace: `AplNet.Core` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public sealed class DefaultParallelismProvider : IParallelismProvider
```

Uses `Environment.ProcessorCount`.

## Constructors

### DefaultParallelismProvider()

```csharp
public DefaultParallelismProvider();
```

## Properties

### DegreeOfParallelism

```csharp
public int DegreeOfParallelism { get; }
```

The number of workers a loop should use when none is specified.

### Instance

```csharp
public static DefaultParallelismProvider Instance { get; }
```

The shared instance.
