# FusedMultiplyAddOperator&lt;T&gt; struct

Namespace: `AplNet.Simd` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public readonly struct FusedMultiplyAddOperator<T> : IUnaryOperator<T>
    where T : unmanaged, IFloatingPointIeee754<T>
```

`x * multiplier + addend` as a fused multiply-add: one rounding instead of the two of [`MultiplyAddOperator<T>`](AplNet.Simd.MultiplyAddOperator-1.md). The explicit opt-in, for `float` and `double`. Results match `Math.FusedMultiplyAdd` bit for bit in every lane, so they can differ from [`MultiplyAddOperator<T>`](AplNet.Simd.MultiplyAddOperator-1.md) in the last bit. On hardware without FMA instructions the runtime emulates it exactly, which is slow.

## Constructors

### FusedMultiplyAddOperator(T, T)

```csharp
public FusedMultiplyAddOperator(T multiplier, T addend);
```

`x * multiplier + addend` as a fused multiply-add: one rounding instead of the two of [`MultiplyAddOperator<T>`](AplNet.Simd.MultiplyAddOperator-1.md). The explicit opt-in, for `float` and `double`. Results match `Math.FusedMultiplyAdd` bit for bit in every lane, so they can differ from [`MultiplyAddOperator<T>`](AplNet.Simd.MultiplyAddOperator-1.md) in the last bit. On hardware without FMA instructions the runtime emulates it exactly, which is slow.

## Properties

### Addend

```csharp
public T Addend { get; }
```

The addend `b` in `fma(x, a, b)`.

### Multiplier

```csharp
public T Multiplier { get; }
```

The multiplier `a` in `fma(x, a, b)`.

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
