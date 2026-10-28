# ParallelExecutor class

Namespace: `AplNet.Core` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public static class ParallelExecutor
```

The struct-invoker core that every APL.Net loop runs on. [`Apl`](AplNet.Apl.md) is the friendlier entry point; these are the same operations without the delegate overloads.

**Remarks**

**ExecutionContext does not flow.** Work is queued with `ThreadPool.UnsafeQueueUserWorkItem`, so a body running on a pool worker does not see the caller's `AsyncLocal<T>` values, and does not inherit culture or impersonation flowed through the execution context. Iterations the calling thread runs itself do see them. `Parallel.For` flows the context to every iteration; if a body depends on ambient state, pass it in explicitly (a field on the body struct) or use TPL.

Failures follow TPL: every exception thrown by the body is collected and rethrown as one `AggregateException` once all running workers have stopped; cancellation through the options' token surfaces as `OperationCanceledException`.

## Methods

### For&lt;TBody&gt;(int, int, TBody, AplOptions)

```csharp
public static void For<TBody>(int fromInclusive, int toExclusive, TBody body, AplOptions? options = null)
    where TBody : struct, IWorkBody;
```

Runs `body` once for every index in `[fromInclusive, toExclusive)`.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### For&lt;TBody&gt;(long, long, TBody, AplOptions)

```csharp
public static void For<TBody>(long fromInclusive, long toExclusive, TBody body, AplOptions? options = null)
    where TBody : struct, ILongWorkBody;
```

Runs `body` once for every index in the 64-bit range `[fromInclusive, toExclusive)`.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### ForRange&lt;TBody&gt;(int, int, TBody, AplOptions)

```csharp
public static void ForRange<TBody>(int fromInclusive, int toExclusive, TBody body, AplOptions? options = null)
    where TBody : struct, IRangeWorkBody;
```

Runs `body` over non-overlapping sub-ranges that together cover `[fromInclusive, toExclusive)`.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### ForRange&lt;TBody&gt;(long, long, TBody, AplOptions)

```csharp
public static void ForRange<TBody>(long fromInclusive, long toExclusive, TBody body, AplOptions? options = null)
    where TBody : struct, ILongRangeWorkBody;
```

Runs `body` over non-overlapping sub-ranges that together cover the 64-bit range `[fromInclusive, toExclusive)`.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### ForWithState&lt;TBody&gt;(int, int, TBody, AplOptions)

```csharp
public static AplLoopResult ForWithState<TBody>(int fromInclusive, int toExclusive, TBody body, AplOptions? options = null)
    where TBody : struct, IStatefulWorkBody;
```

Runs `body` for every index in `[fromInclusive, toExclusive)`, letting it end the loop early through [`AplLoopState`](AplNet.Core.AplLoopState.md).

**Returns:** Whether the loop ran to completion, and the lowest break index if it did not.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.

### Reduce&lt;T, TBody&gt;(int, int, T, TBody, AplOptions)

```csharp
public static T Reduce<T, TBody>(int fromInclusive, int toExclusive, T identity, TBody body, AplOptions? options = null)
    where TBody : struct, IReduceBody<T>;
```

Folds every index in `[fromInclusive, toExclusive)` into one value: each worker accumulates from `identity`, and the partial results are combined in partition order. Returns `identity` for an empty range.

**Exceptions**

- `AggregateException`: The body threw; contains every exception thrown.
- `OperationCanceledException`: The options' token was canceled.
