# SqrtOperator&lt;T&gt; struct

Namespace: `AplNet.Simd` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public readonly struct SqrtOperator<T> : IUnaryOperator<T>
    where T : unmanaged, INumber<T>, IRootFunctions<T>
```

`sqrt(x)`, for `float` and `double`.

## Methods

### Invoke(T)

```csharp
public T Invoke(T x);
```

Applies the function to one value.

### Invoke(Vector128&lt;T&gt;)

```csharp
public Vector128<T> Invoke(Vector128<T> x);
```

Applies the function to every lane.

### Invoke(Vector256&lt;T&gt;)

```csharp
public Vector256<T> Invoke(Vector256<T> x);
```

Applies the function to every lane.

### Invoke(Vector512&lt;T&gt;)

```csharp
public Vector512<T> Invoke(Vector512<T> x);
```

Applies the function to every lane.
