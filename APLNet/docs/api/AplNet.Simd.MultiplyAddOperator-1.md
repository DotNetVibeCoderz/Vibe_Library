# MultiplyAddOperator&lt;T&gt; struct

Namespace: `AplNet.Simd` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public readonly struct MultiplyAddOperator<T> : IUnaryOperator<T>
    where T : unmanaged, INumber<T>
```

`x * multiplier + addend`: the AXPY-style affine map. Computed as a multiply then an add, so results match the scalar expression bit for bit (a fused multiply-add would round once instead of twice).

## Constructors

### MultiplyAddOperator(T, T)

```csharp
public MultiplyAddOperator(T multiplier, T addend);
```

`x * multiplier + addend`: the AXPY-style affine map. Computed as a multiply then an add, so results match the scalar expression bit for bit (a fused multiply-add would round once instead of twice).

## Properties

### Addend

```csharp
public T Addend { get; }
```

The addend `b` in `x * a + b`.

### Multiplier

```csharp
public T Multiplier { get; }
```

The multiplier `a` in `x * a + b`.

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
