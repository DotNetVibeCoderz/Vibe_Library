# Apl class

Namespace: `AplNet` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public static class Apl
```

APL.Net's entry point: data-parallel loops with the call shape of `Parallel` and far less overhead per iteration.

**Remarks**

Each operation comes in two flavours. The delegate overloads are drop-in replacements for `Parallel.For` / `Parallel.ForEach`. The generic struct overloads (`TBody : struct, IWorkBody`) are the zero-allocation path: the JIT specialises the loop for your struct and inlines its `Invoke`, so there is no delegate call per iteration at all. [`Apl.ForRange<T>`](AplNet.Apl.md) goes further and hands the body whole sub-ranges.

**Behavioural difference from TPL: the `ExecutionContext` does not flow to pool workers.** Bodies running on them do not see the caller's `AsyncLocal<T>` values or flowed culture/impersonation. This is deliberate - capturing and restoring the context is pure overhead for numeric loops - but a body that relies on ambient state must receive it explicitly, or use TPL. The async wrappers ([`Apl.ForEachAsync<T>`](AplNet.Apl.md) and friends) are the exception: they are built on TPL and do flow it.

Work is split statically by default ([`StaticRangePartitioner`](AplNet.Core.Partitioners.StaticRangePartitioner.md)). If iteration costs vary widely, pass [`WorkStealingPartitioner`](AplNet.Core.Partitioners.WorkStealingPartitioner.md) in [`AplOptions.Partitioner`](AplNet.AplOptions.md).

## Methods

### For(int, int, Action&lt;int, AplLoopState&gt;, AplOptions)

```csharp
public static AplLoopResult For(int fromInclusive, int toExclusive, Action<int, AplLoopState> body, AplOptions? options = null);
```

Runs `body` for every index, letting it end the loop early with [`AplLoopState.Stop`](AplNet.Core.AplLoopState.md) or [`AplLoopState.Break`](AplNet.Core.AplLoopState.md). Drop-in for `Parallel.For` with a `ParallelLoopState`.

**Returns:** Whether the loop ran to completion, and the lowest break index if it did not.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### For(int, int, Action&lt;int&gt;, AplOptions)

```csharp
public static void For(int fromInclusive, int toExclusive, Action<int> body, AplOptions? options = null);
```

Runs `body` for every index in `[fromInclusive, toExclusive)`. Drop-in for `Parallel.For`.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### For(long, long, Action&lt;long&gt;, AplOptions)

```csharp
public static void For(long fromInclusive, long toExclusive, Action<long> body, AplOptions? options = null);
```

Runs `body` for every index of a 64-bit range, for loops over more than `Int32.MaxValue` items.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### For&lt;TBody&gt;(int, int, TBody, AplOptions)

```csharp
public static void For<TBody>(int fromInclusive, int toExclusive, TBody body, AplOptions? options = null)
    where TBody : struct, IWorkBody;
```

Runs a struct body for every index in `[fromInclusive, toExclusive)`. Allocation-free in steady state, and the JIT inlines the body's [`IWorkBody.Invoke`](AplNet.Core.IWorkBody.md) into the loop.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### For&lt;TBody&gt;(long, long, TBody, AplOptions)

```csharp
public static void For<TBody>(long fromInclusive, long toExclusive, TBody body, AplOptions? options = null)
    where TBody : struct, ILongWorkBody;
```

Runs a struct body for every index of a 64-bit range. Allocation-free in steady state.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### ForAsync(int, int, Func&lt;int, CancellationToken, ValueTask&gt;, AplOptions)

```csharp
public static Task ForAsync(int fromInclusive, int toExclusive, Func<int, CancellationToken, ValueTask> body, AplOptions? options = null);
```

Runs an asynchronous body for every index. A thin wrapper over `Parallel.ForAsync` that accepts [`AplOptions`](AplNet.AplOptions.md): asynchronous bodies are dominated by I/O, not by dispatch, so there is nothing for APL.Net's machinery to win. Unlike the synchronous loops this does flow the `ExecutionContext`.

### ForEach&lt;T, TBody&gt;(Memory&lt;T&gt;, TBody, AplOptions)

```csharp
public static void ForEach<T, TBody>(Memory<T> source, TBody body, AplOptions? options = null)
    where TBody : struct, IWorkBody<T>;
```

Runs a struct body for every element of `source`, by reference. Allocation-free in steady state.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### ForEach&lt;T, TBody&gt;(T[], TBody, AplOptions)

```csharp
public static void ForEach<T, TBody>(T[] source, TBody body, AplOptions? options = null)
    where TBody : struct, IWorkBody<T>;
```

Runs a struct body for every element of `source`, by reference. Allocation-free in steady state.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### ForEach&lt;T&gt;(T[], Action&lt;T&gt;, AplOptions)

```csharp
public static void ForEach<T>(T[] source, Action<T> body, AplOptions? options = null);
```

Runs `body` for every element of `source`. Drop-in for `Parallel.ForEach` over an array.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### ForEach&lt;T&gt;(T[], RefItemAction&lt;T&gt;, AplOptions)

```csharp
public static void ForEach<T>(T[] source, RefItemAction<T> body, AplOptions? options = null);
```

Runs `body` for every element of `source`, by reference so it can be updated in place.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### ForEachAsync&lt;T&gt;(IAsyncEnumerable&lt;T&gt;, Func&lt;T, CancellationToken, ValueTask&gt;, AplOptions)

```csharp
public static Task ForEachAsync<T>(IAsyncEnumerable<T> source, Func<T, CancellationToken, ValueTask> body, AplOptions? options = null);
```

Runs an asynchronous body for every element of an async stream. See [`Apl.ForAsync`](AplNet.Apl.md) for why this wraps TPL.

### ForEachAsync&lt;T&gt;(IEnumerable&lt;T&gt;, Func&lt;T, CancellationToken, ValueTask&gt;, AplOptions)

```csharp
public static Task ForEachAsync<T>(IEnumerable<T> source, Func<T, CancellationToken, ValueTask> body, AplOptions? options = null);
```

Runs an asynchronous body for every element. See [`Apl.ForAsync`](AplNet.Apl.md) for why this wraps TPL.

### ForRange(int, int, Action&lt;int, int&gt;, AplOptions)

```csharp
public static void ForRange(int fromInclusive, int toExclusive, Action<int, int> body, AplOptions? options = null);
```

Runs `body` once per sub-range; the ranges are disjoint and cover `[fromInclusive, toExclusive)`.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### ForRange(long, long, Action&lt;long, long&gt;, AplOptions)

```csharp
public static void ForRange(long fromInclusive, long toExclusive, Action<long, long> body, AplOptions? options = null);
```

Runs `body` once per sub-range of a 64-bit range; the ranges are disjoint and cover `[fromInclusive, toExclusive)`.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### ForRange&lt;TBody&gt;(int, int, TBody, AplOptions)

```csharp
public static void ForRange<TBody>(int fromInclusive, int toExclusive, TBody body, AplOptions? options = null)
    where TBody : struct, IRangeWorkBody;
```

Runs a struct body once per sub-range. The fastest loop shape: the body can slice spans (no bounds checks), hoist invariants and use SIMD across its whole range.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### ForRange&lt;TBody&gt;(long, long, TBody, AplOptions)

```csharp
public static void ForRange<TBody>(long fromInclusive, long toExclusive, TBody body, AplOptions? options = null)
    where TBody : struct, ILongRangeWorkBody;
```

Runs a struct body once per sub-range of a 64-bit range. Allocation-free in steady state.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### ForWithState&lt;TBody&gt;(int, int, TBody, AplOptions)

```csharp
public static AplLoopResult ForWithState<TBody>(int fromInclusive, int toExclusive, TBody body, AplOptions? options = null)
    where TBody : struct, IStatefulWorkBody;
```

Runs a struct body for every index, letting it end the loop early through [`AplLoopState`](AplNet.Core.AplLoopState.md). Allocation-free in steady state. The state is checked before every iteration, at the cost of one load and one compare.

**Returns:** Whether the loop ran to completion, and the lowest break index if it did not.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### Reduce&lt;T, TBody&gt;(int, int, T, TBody, AplOptions)

```csharp
public static T Reduce<T, TBody>(int fromInclusive, int toExclusive, T identity, TBody body, AplOptions? options = null)
    where TBody : struct, IReduceBody<T>;
```

Folds `[fromInclusive, toExclusive)` into one value with a struct body. Allocation-free in steady state.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### Reduce&lt;T&gt;(int, int, T, Func&lt;int, int, T, T&gt;, Func&lt;T, T, T&gt;, AplOptions)

```csharp
public static T Reduce<T>(int fromInclusive, int toExclusive, T identity, Func<int, int, T, T> accumulate, Func<T, T, T> combine, AplOptions? options = null);
```

Folds `[fromInclusive, toExclusive)` into one value. `accumulate` folds a sub-range into a worker's running value; `combine` merges two workers' values and must be associative, with `identity` as its identity.

**Exceptions**

- `AggregateException`: A callback threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.
