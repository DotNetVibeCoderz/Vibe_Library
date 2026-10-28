# IWorkBody&lt;T&gt; interface

Namespace: `AplNet.Core` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public interface IWorkBody<T>
```

A `ForEach` body that receives each element by reference, so it can read or replace it in place without the copy a by-value delegate would make.

**Type parameters**

- `T`: The element type.

## Methods

### Invoke(int, T)

```csharp
public void Invoke(int index, ref T item);
```

Runs one iteration over `item`, the element at `index`.
