# AplBodyAttribute class

Namespace: `AplNet` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public sealed class AplBodyAttribute : Attribute
```

Marks a static method as a loop body. The APL.Net source generator emits a `readonly struct` implementing [`IWorkBody`](AplNet.Core.IWorkBody.md) that calls it, plus a factory method on `AplNet.AplGen` named after the method.

**Remarks**

The method must be `static`, return `void`, take the loop index as its first parameter (`int`), and be at least `internal`. Every further parameter becomes a field of the generated struct and a parameter of the factory:

[AplBody] internal static void Scale(int i, double[] data, double factor) => data[i] *= factor; Apl.For(0, data.Length, AplGen.Scale(data, 2.0));

This gives the struct-invoker's zero-allocation, inlined dispatch without writing the struct by hand, and without runtime reflection - it is all resolved at compile time, so it works under NativeAOT.

## Constructors

### AplBodyAttribute()

```csharp
public AplBodyAttribute();
```
