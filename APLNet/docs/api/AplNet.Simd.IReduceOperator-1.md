# IReduceOperator&lt;T&gt; interface

Namespace: `AplNet.Simd` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public interface IReduceOperator<T> : IBinaryOperator<T>
    where T : struct
```

A monoid for reductions: an associative (and, for parallel use, commutative) binary operation with an identity, plus the horizontal reduction of one vector to a scalar. Sum, min and max are built in; implement this for a custom one.

**Type parameters**

- `T`: A primitive numeric type.

## Properties

### Identity

```csharp
public T Identity { get; }
```

The identity: `Invoke(Identity, x) == x` for every `x`.

## Methods

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
