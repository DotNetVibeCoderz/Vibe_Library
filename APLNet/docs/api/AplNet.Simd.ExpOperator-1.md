# ExpOperator&lt;T&gt; struct

Namespace: `AplNet.Simd` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public readonly struct ExpOperator<T> : IUnaryOperator<T>
    where T : unmanaged, IExponentialFunctions<T>
```

`e^x`, for `float` and `double`. The vector form is the runtime's own vectorized `Exp`: accurate to within a few ulp of `Math.Exp`, not bit-identical to it, so a vectorized result can differ from the scalar tail in the last digits.

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
