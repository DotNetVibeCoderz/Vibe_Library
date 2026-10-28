# UnsafeParallel class

Namespace: `AplNet.Unsafe` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public static class UnsafeParallel
```

**UNSAFE.** Parallel loops over raw pointers with function-pointer bodies: no delegates, no bounds checks, no range validation. The "you asked for it" tier.

**Remarks**

**The caller guarantees** that every address the body touches stays valid for the whole call: the memory must be pinned (`fixed`, `GCHandle`, [`NativeBuffer<T>`](AplNet.Unsafe.NativeBuffer-1.md), `NativeMemory`) and must not be freed until the method returns. Nothing here checks `ptr + i` against anything; an off-by-one is memory corruption, not an exception.

Bodies are `delegate*` function pointers, so they must be `static` methods (no closures). Taking one with `&Method` allocates nothing, and the call is a plain indirect call.

Scheduling is shared with the safe API: the same partitioners, options, pooled jobs and absence of `ExecutionContext` flow. So is exception handling - a managed exception thrown by a body is still collected into an `AggregateException` rather than tearing the process down, because in .NET that costs nothing until something actually throws. An access violation is a different matter: it cannot be caught and will terminate the process.

## Methods

### For(void*, int, int, , AplOptions)

```csharp
public static void For(void* context, int fromInclusive, int toExclusive,  body, AplOptions? options = null);
```

### ForPtr(double*, int, , int?)

```csharp
public static void ForPtr(double* ptr, int length,  body, int? maxDegreeOfParallelism = null);
```

### ForPtr&lt;T&gt;(T*, int, , AplOptions)

```csharp
public static void ForPtr<T>(T* ptr, int length,  body, AplOptions? options = null)
    where T : unmanaged;
```

### ForRangePtr&lt;T&gt;(T*, int, , AplOptions)

```csharp
public static void ForRangePtr<T>(T* ptr, int length,  body, AplOptions? options = null)
    where T : unmanaged;
```
