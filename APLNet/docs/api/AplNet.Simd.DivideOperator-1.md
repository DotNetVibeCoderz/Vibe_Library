# DivideOperator&lt;T&gt; struct

Namespace: `AplNet.Simd` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public readonly struct DivideOperator<T> : IBinaryOperator<T>
    where T : unmanaged, INumber<T>
```

`x / y`. Integer division has no SIMD instruction on x86, so for integers this is correct but not fast.

## Methods

### Invoke(T, T)

```csharp
public T Invoke(T x, T y);
```

Applies the function to one pair.

### Invoke(Vector128&lt;T&gt;, Vector128&lt;T&gt;)

```csharp
public Vector128<T> Invoke(Vector128<T> x, Vector128<T> y);
```

Applies the function lane by lane.

### Invoke(Vector256&lt;T&gt;, Vector256&lt;T&gt;)

```csharp
public Vector256<T> Invoke(Vector256<T> x, Vector256<T> y);
```

Applies the function lane by lane.

### Invoke(Vector512&lt;T&gt;, Vector512&lt;T&gt;)

```csharp
public Vector512<T> Invoke(Vector512<T> x, Vector512<T> y);
```

Applies the function lane by lane.
