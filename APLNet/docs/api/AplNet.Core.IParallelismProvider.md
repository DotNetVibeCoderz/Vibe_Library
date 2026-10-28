# IParallelismProvider interface

Namespace: `AplNet.Core` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public interface IParallelismProvider
```

Supplies the default degree of parallelism. Exists so scheduling can be tested independently of the machine it runs on.

## Properties

### DegreeOfParallelism

```csharp
public int DegreeOfParallelism { get; }
```

The number of workers a loop should use when none is specified.
