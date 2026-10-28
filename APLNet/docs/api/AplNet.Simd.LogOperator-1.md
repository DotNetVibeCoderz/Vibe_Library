# LogOperator&lt;T&gt; struct

Namespace: `AplNet.Simd` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public readonly struct LogOperator<T> : IUnaryOperator<T>
    where T : unmanaged, ILogarithmicFunctions<T>
```

The natural logarithm `ln(x)`, for `float` and `double`. Like [`ExpOperator<T>`](AplNet.Simd.ExpOperator-1.md), the vector form is within a few ulp of the scalar one rather than bit-identical to it.

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
