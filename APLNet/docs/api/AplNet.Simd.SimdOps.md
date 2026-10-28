# SimdOps class

Namespace: `AplNet.Simd` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public static class SimdOps
```

SIMD bulk operations - map, map-reduce and in-place transform - single-threaded over spans and parallel over arrays or `Memory<T>`.

**Remarks**

Operations are expressed with operator structs ([`IUnaryOperator<T>`](AplNet.Simd.IUnaryOperator-1.md), [`IBinaryOperator<T>`](AplNet.Simd.IBinaryOperator-1.md), [`IReduceOperator<T>`](AplNet.Simd.IReduceOperator-1.md)); the kernels are generic over them, so every operator call is inlined. Built-in operators cover the common cases ([`MultiplyAddOperator<T>`](AplNet.Simd.MultiplyAddOperator-1.md), [`AddOperator<T>`](AplNet.Simd.AddOperator-1.md), [`MinOperator<T>`](AplNet.Simd.MinOperator-1.md), ...). Supported element types are the primitive numerics the .NET vector types support: at least `float`, `double`, `int` and `long`, plus the other integer widths.

The parallel methods take arrays or `Memory<T>` rather than spans because a span cannot be handed to another thread; each worker turns its slice back into a span and runs the same single-threaded kernel over it. They split no finer than [`SimdOps.DefaultMinChunkSize`](AplNet.Simd.SimdOps.md) elements per worker unless [`AplOptions.MinChunkSize`](AplNet.AplOptions.md) says otherwise: a vector loop gets through 32K floats in a few microseconds, about what it costs to wake a pool thread.

## Fields

### DefaultMinChunkSize

```csharp
public const int DefaultMinChunkSize = 32768;
```

The fewest elements a parallel SIMD operation gives one worker, unless the options override it.

## Methods

### Dot&lt;T&gt;(ReadOnlySpan&lt;T&gt;, ReadOnlySpan&lt;T&gt;)

```csharp
public static T Dot<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y)
    where T : unmanaged, INumber<T>;
```

The dot product `x[0]*y[0] + x[1]*y[1] + ...`.

**Exceptions**

- `ArgumentException`: The inputs differ in length.

### MapReduce&lt;T, TMap, TReduce&gt;(ReadOnlySpan&lt;T&gt;, TMap, TReduce)

```csharp
public static T MapReduce<T, TMap, TReduce>(ReadOnlySpan<T> source, TMap map, TReduce reduce)
    where T : struct
    where TMap : struct, IUnaryOperator<T>
    where TReduce : struct, IReduceOperator<T>;
```

`reduce(map(source[0]), map(source[1]), ...)`, e.g. a sum of squares.

### MapReduce&lt;T, TMap, TReduce&gt;(ReadOnlySpan&lt;T&gt;, ReadOnlySpan&lt;T&gt;, TMap, TReduce)

```csharp
public static T MapReduce<T, TMap, TReduce>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, TMap map, TReduce reduce)
    where T : struct
    where TMap : struct, IBinaryOperator<T>
    where TReduce : struct, IReduceOperator<T>;
```

`reduce(map(x[0], y[0]), map(x[1], y[1]), ...)`, e.g. a dot product.

**Exceptions**

- `ArgumentException`: The inputs differ in length.

### Max&lt;T&gt;(ReadOnlySpan&lt;T&gt;)

```csharp
public static T Max<T>(ReadOnlySpan<T> source)
    where T : unmanaged, INumber<T>;
```

The largest element.

**Exceptions**

- `InvalidOperationException`: `source` is empty.

### Min&lt;T&gt;(ReadOnlySpan&lt;T&gt;)

```csharp
public static T Min<T>(ReadOnlySpan<T> source)
    where T : unmanaged, INumber<T>;
```

The smallest element.

**Exceptions**

- `InvalidOperationException`: `source` is empty.

### ParallelDot&lt;T&gt;(ReadOnlyMemory&lt;T&gt;, ReadOnlyMemory&lt;T&gt;, AplOptions)

```csharp
public static T ParallelDot<T>(ReadOnlyMemory<T> x, ReadOnlyMemory<T> y, AplOptions? options = null)
    where T : unmanaged, INumber<T>;
```

The dot product, parallel and vectorized.

**Exceptions**

- `ArgumentException`: The inputs differ in length.

### ParallelDot&lt;T&gt;(T[], T[], AplOptions)

```csharp
public static T ParallelDot<T>(T[] x, T[] y, AplOptions? options = null)
    where T : unmanaged, INumber<T>;
```

The dot product, parallel and vectorized.

### ParallelMapReduce&lt;T, TMap, TReduce&gt;(ReadOnlyMemory&lt;T&gt;, TMap, TReduce, AplOptions)

```csharp
public static T ParallelMapReduce<T, TMap, TReduce>(ReadOnlyMemory<T> source, TMap map, TReduce reduce, AplOptions? options = null)
    where T : struct
    where TMap : struct, IUnaryOperator<T>
    where TReduce : struct, IReduceOperator<T>;
```

The parallel form of [`SimdOps.MapReduce<T, T2, T3>`](AplNet.Simd.SimdOps.md).

### ParallelMapReduce&lt;T, TMap, TReduce&gt;(ReadOnlyMemory&lt;T&gt;, ReadOnlyMemory&lt;T&gt;, TMap, TReduce, AplOptions)

```csharp
public static T ParallelMapReduce<T, TMap, TReduce>(ReadOnlyMemory<T> x, ReadOnlyMemory<T> y, TMap map, TReduce reduce, AplOptions? options = null)
    where T : struct
    where TMap : struct, IBinaryOperator<T>
    where TReduce : struct, IReduceOperator<T>;
```

The parallel form of [`SimdOps.MapReduce<T, T2, T3>`](AplNet.Simd.SimdOps.md).

**Exceptions**

- `ArgumentException`: The inputs differ in length.

### ParallelMapReduce&lt;T, TMap, TReduce&gt;(T[], TMap, TReduce, AplOptions)

```csharp
public static T ParallelMapReduce<T, TMap, TReduce>(T[] source, TMap map, TReduce reduce, AplOptions? options = null)
    where T : struct
    where TMap : struct, IUnaryOperator<T>
    where TReduce : struct, IReduceOperator<T>;
```

The parallel form of [`SimdOps.MapReduce<T, T2, T3>`](AplNet.Simd.SimdOps.md).

### ParallelMapReduce&lt;T, TMap, TReduce&gt;(T[], T[], TMap, TReduce, AplOptions)

```csharp
public static T ParallelMapReduce<T, TMap, TReduce>(T[] x, T[] y, TMap map, TReduce reduce, AplOptions? options = null)
    where T : struct
    where TMap : struct, IBinaryOperator<T>
    where TReduce : struct, IReduceOperator<T>;
```

The parallel form of [`SimdOps.MapReduce<T, T2, T3>`](AplNet.Simd.SimdOps.md).

### ParallelMax&lt;T&gt;(ReadOnlyMemory&lt;T&gt;, AplOptions)

```csharp
public static T ParallelMax<T>(ReadOnlyMemory<T> source, AplOptions? options = null)
    where T : unmanaged, INumber<T>;
```

The largest element, parallel and vectorized.

**Exceptions**

- `InvalidOperationException`: `source` is empty.

### ParallelMax&lt;T&gt;(T[], AplOptions)

```csharp
public static T ParallelMax<T>(T[] source, AplOptions? options = null)
    where T : unmanaged, INumber<T>;
```

The largest element, parallel and vectorized.

**Exceptions**

- `InvalidOperationException`: `source` is empty.

### ParallelMin&lt;T&gt;(ReadOnlyMemory&lt;T&gt;, AplOptions)

```csharp
public static T ParallelMin<T>(ReadOnlyMemory<T> source, AplOptions? options = null)
    where T : unmanaged, INumber<T>;
```

The smallest element, parallel and vectorized.

**Exceptions**

- `InvalidOperationException`: `source` is empty.

### ParallelMin&lt;T&gt;(T[], AplOptions)

```csharp
public static T ParallelMin<T>(T[] source, AplOptions? options = null)
    where T : unmanaged, INumber<T>;
```

The smallest element, parallel and vectorized.

**Exceptions**

- `InvalidOperationException`: `source` is empty.

### ParallelReduce&lt;T, TOp&gt;(ReadOnlyMemory&lt;T&gt;, TOp, AplOptions)

```csharp
public static T ParallelReduce<T, TOp>(ReadOnlyMemory<T> source, TOp op, AplOptions? options = null)
    where T : struct
    where TOp : struct, IReduceOperator<T>;
```

Folds `source` with `op` across all workers; returns its identity when empty.

### ParallelReduce&lt;T, TOp&gt;(T[], TOp, AplOptions)

```csharp
public static T ParallelReduce<T, TOp>(T[] source, TOp op, AplOptions? options = null)
    where T : struct
    where TOp : struct, IReduceOperator<T>;
```

Folds `source` with `op` across all workers; returns its identity when empty.

### ParallelSum&lt;T&gt;(ReadOnlyMemory&lt;T&gt;, AplOptions)

```csharp
public static T ParallelSum<T>(ReadOnlyMemory<T> source, AplOptions? options = null)
    where T : unmanaged, INumber<T>;
```

The sum of `source`, parallel and vectorized; zero when empty.

### ParallelSum&lt;T&gt;(T[], AplOptions)

```csharp
public static T ParallelSum<T>(T[] source, AplOptions? options = null)
    where T : unmanaged, INumber<T>;
```

The sum of `source`, parallel and vectorized; zero when empty.

### ParallelTransform&lt;T, TOp&gt;(ReadOnlyMemory&lt;T&gt;, Memory&lt;T&gt;, TOp, AplOptions)

```csharp
public static void ParallelTransform<T, TOp>(ReadOnlyMemory<T> source, Memory<T> destination, TOp op, AplOptions? options = null)
    where T : struct
    where TOp : struct, IUnaryOperator<T>;
```

`destination[i] = op(source[i])` across all workers, vectorized within each.

**Exceptions**

- `ArgumentException`: `destination` is shorter than `source`, or they partially overlap.

### ParallelTransform&lt;T, TOp&gt;(ReadOnlyMemory&lt;T&gt;, ReadOnlyMemory&lt;T&gt;, Memory&lt;T&gt;, TOp, AplOptions)

```csharp
public static void ParallelTransform<T, TOp>(ReadOnlyMemory<T> x, ReadOnlyMemory<T> y, Memory<T> destination, TOp op, AplOptions? options = null)
    where T : struct
    where TOp : struct, IBinaryOperator<T>;
```

`destination[i] = op(x[i], y[i])` across all workers, vectorized within each.

**Exceptions**

- `ArgumentException`: The inputs differ in length, `destination` is too short, or it partially overlaps an input.

### ParallelTransform&lt;T, TOp&gt;(T[], T[], TOp, AplOptions)

```csharp
public static void ParallelTransform<T, TOp>(T[] source, T[] destination, TOp op, AplOptions? options = null)
    where T : struct
    where TOp : struct, IUnaryOperator<T>;
```

`destination[i] = op(source[i])` across all workers, vectorized within each.

### ParallelTransform&lt;T, TOp&gt;(T[], T[], T[], TOp, AplOptions)

```csharp
public static void ParallelTransform<T, TOp>(T[] x, T[] y, T[] destination, TOp op, AplOptions? options = null)
    where T : struct
    where TOp : struct, IBinaryOperator<T>;
```

`destination[i] = op(x[i], y[i])` across all workers, vectorized within each.

### ParallelTransformInPlace&lt;T, TOp&gt;(Memory&lt;T&gt;, TOp, AplOptions)

```csharp
public static void ParallelTransformInPlace<T, TOp>(Memory<T> data, TOp op, AplOptions? options = null)
    where T : struct
    where TOp : struct, IUnaryOperator<T>;
```

`data[i] = op(data[i])` across all workers, vectorized within each.

### ParallelTransformInPlace&lt;T, TOp&gt;(T[], TOp, AplOptions)

```csharp
public static void ParallelTransformInPlace<T, TOp>(T[] data, TOp op, AplOptions? options = null)
    where T : struct
    where TOp : struct, IUnaryOperator<T>;
```

`data[i] = op(data[i])` across all workers, vectorized within each.

### ParallelTransformInPlace&lt;T&gt;(Memory&lt;T&gt;, Func&lt;Vector256&lt;T&gt;, Vector256&lt;T&gt;&gt;, Func&lt;T, T&gt;, AplOptions)

```csharp
public static void ParallelTransformInPlace<T>(Memory<T> data, Func<Vector256<T>, Vector256<T>> vectorOp, Func<T, T> scalarOp, AplOptions? options = null)
    where T : struct;
```

The parallel form of [`SimdOps.TransformInPlace<T>`](AplNet.Simd.SimdOps.md).

### ParallelTransformInPlace&lt;T&gt;(T[], Func&lt;Vector256&lt;T&gt;, Vector256&lt;T&gt;&gt;, Func&lt;T, T&gt;, AplOptions)

```csharp
public static void ParallelTransformInPlace<T>(T[] data, Func<Vector256<T>, Vector256<T>> vectorOp, Func<T, T> scalarOp, AplOptions? options = null)
    where T : struct;
```

The parallel form of [`SimdOps.TransformInPlace<T>`](AplNet.Simd.SimdOps.md).

### Reduce&lt;T, TOp&gt;(ReadOnlySpan&lt;T&gt;, TOp)

```csharp
public static T Reduce<T, TOp>(ReadOnlySpan<T> source, TOp op)
    where T : struct
    where TOp : struct, IReduceOperator<T>;
```

Folds `source` with `op`; returns its identity when empty.

### Sum&lt;T&gt;(ReadOnlySpan&lt;T&gt;)

```csharp
public static T Sum<T>(ReadOnlySpan<T> source)
    where T : unmanaged, INumber<T>;
```

The sum of `source`; zero when empty.

### SumOfSquares&lt;T&gt;(ReadOnlySpan&lt;T&gt;)

```csharp
public static T SumOfSquares<T>(ReadOnlySpan<T> source)
    where T : unmanaged, INumber<T>;
```

The sum of the squares of `source`.

### Transform&lt;T, TOp&gt;(ReadOnlySpan&lt;T&gt;, Span&lt;T&gt;, TOp)

```csharp
public static void Transform<T, TOp>(ReadOnlySpan<T> source, Span<T> destination, TOp op)
    where T : struct
    where TOp : struct, IUnaryOperator<T>;
```

`destination[i] = op(source[i])`, vectorized. `destination` may be the same memory as `source`, but must not partially overlap it.

**Exceptions**

- `ArgumentException`: `destination` is shorter than `source`, or they partially overlap.

### Transform&lt;T, TOp&gt;(ReadOnlySpan&lt;T&gt;, ReadOnlySpan&lt;T&gt;, Span&lt;T&gt;, TOp)

```csharp
public static void Transform<T, TOp>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, Span<T> destination, TOp op)
    where T : struct
    where TOp : struct, IBinaryOperator<T>;
```

`destination[i] = op(x[i], y[i])`, vectorized.

**Exceptions**

- `ArgumentException`: The inputs differ in length, `destination` is too short, or it partially overlaps an input.

### TransformInPlace&lt;T, TOp&gt;(Span&lt;T&gt;, TOp)

```csharp
public static void TransformInPlace<T, TOp>(Span<T> data, TOp op)
    where T : struct
    where TOp : struct, IUnaryOperator<T>;
```

`data[i] = op(data[i])`, vectorized.

### TransformInPlace&lt;T&gt;(Span&lt;T&gt;, Func&lt;Vector256&lt;T&gt;, Vector256&lt;T&gt;&gt;, Func&lt;T, T&gt;)

```csharp
public static void TransformInPlace<T>(Span<T> data, Func<Vector256<T>, Vector256<T>> vectorOp, Func<T, T> scalarOp)
    where T : struct;
```

`data[i] = op(data[i])` with the operation given as delegates: `vectorOp` for each group of `Vector256<T>.Count` elements, `scalarOp` for the rest - and for everything when the CPU has no 256-bit vectors. Costs a delegate call per vector; an operator struct costs nothing.
