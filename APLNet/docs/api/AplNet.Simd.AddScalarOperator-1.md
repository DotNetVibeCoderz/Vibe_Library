# AddScalarOperator&lt;T&gt; struct

Namespace: `AplNet.Simd` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public readonly struct AddScalarOperator<T> : IUnaryOperator<T>
    where T : unmanaged, INumber<T>
```

`x + addend`.

## Constructors

### AddScalarOperator(T)

```csharp
public AddScalarOperator(T addend);
```

`x + addend`.

## Properties

### Addend

```csharp
public T Addend { get; }
```

The value added to every element.

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
