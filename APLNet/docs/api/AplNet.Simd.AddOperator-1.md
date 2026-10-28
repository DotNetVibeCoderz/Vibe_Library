# AddOperator&lt;T&gt; struct

Namespace: `AplNet.Simd` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public readonly struct AddOperator<T> : IReduceOperator<T>
    where T : unmanaged, INumber<T>
```

`x + y`; as a reduction, the sum (identity 0).

## Properties

### Identity

```csharp
public T Identity { get; }
```

The identity: `Invoke(Identity, x) == x` for every `x`.

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

### Reduce(Vector128&lt;T&gt;)

```csharp
public T Reduce(Vector128<T> x);
```

Combines every lane of `x` into one value.

### Reduce(Vector256&lt;T&gt;)

```csharp
public T Reduce(Vector256<T> x);
```

Combines every lane of `x` into one value.

### Reduce(Vector512&lt;T&gt;)

```csharp
public T Reduce(Vector512<T> x);
```

Combines every lane of `x` into one value.
