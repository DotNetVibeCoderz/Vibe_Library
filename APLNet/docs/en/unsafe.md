# Unsafe tier (`AplNet.Unsafe`)

[Bahasa Indonesia](../id/unsafe.md) · [Index](README.md)

> **WARNING.** Nothing in this namespace checks addresses. An off-by-one error here corrupts
> memory; it does not throw. Use this tier only when your data already lives in native memory,
> or when a profiler shows that bounds checks in a range body really matter.

## `UnsafeParallel`

| Method | Body signature | Calls |
|---|---|---|
| `ForPtr(double* p, int n, delegate*<double*, int, void>, int? maxDop)` | `(p, i)` | once per element (the spec's signature) |
| `ForPtr<T>(T* p, int n, delegate*<T*, int, void>, AplOptions?)` | `(p, i)` | once per element |
| `ForRangePtr<T>(T* p, int n, delegate*<T*, int, int, void>, AplOptions?)` | `(p, from, to)` | once per slice |
| `For(void* ctx, int from, int to, delegate*<void*, int, int, void>, AplOptions?)` | `(ctx, from, to)` | once per slice; `ctx` usually points at a struct of pointers |

The bodies are **static** methods taken with `&Method`. A function pointer allocates nothing and
cannot capture anything. Scheduling, options and partitioners are the same as in the safe API.

**The caller guarantees** that every address the body touches stays valid and pinned until the
method returns. Use `fixed`, a `GCHandle`, `NativeMemory`, or `NativeBuffer<T>`.

```csharp
[StructLayout(LayoutKind.Sequential)]
unsafe struct AddJob { public float* X, Y, Dst; }

static unsafe void Add(void* ctx, int from, int to)
{
    var job = (AddJob*)ctx;
    for (int i = from; i < to; i++)
        job->Dst[i] = job->X[i] + job->Y[i];
}

fixed (float* x = xs, y = ys, d = dst)
{
    var job = new AddJob { X = x, Y = y, Dst = d };
    UnsafeParallel.For(&job, 0, n, &Add);
}
```

**Exceptions.** A managed exception thrown by a pointer body is collected into an
`AggregateException`, exactly as in the safe API. Unlike the spec's sketch, the process is not
torn down: a `try` region costs nothing until something throws, so there was nothing to gain. An
access violation still cannot be caught, and it ends the process.

## `NativeBuffer<T>`

```csharp
using var buffer = new NativeBuffer<float>(1_000_000);   // zeroed, 64-byte aligned
float* p = buffer.Pointer;
Span<float> s = buffer.Span;
Memory<float> m = buffer.Memory;                           // for SimdOps.Parallel* and Apl.ForEach
ref float first = ref buffer[0];                           // indexer is bounds checked
```

- The buffer is allocated with `NativeMemory.AlignedAlloc`: never moved, never scanned by the
  GC, and aligned to a cache line (and to one AVX-512 vector).
- `Dispose` frees the memory. A finalizer frees it if you forget, but only whenever the GC gets
  around to it.
- After disposal, `Pointer`, `Span` and `Memory` throw `ObjectDisposedException`. A pointer
  you obtained earlier does not.

## When it is worth it

In the Gallery's *Raw pointers* case, `ForRangePtr` lands close to a safe struct body. That is
the expected result: the safe API has already removed the per-element overhead, and the JIT often
hoists bounds checks out of span loops. The pointer tier is for interop-heavy code, where the data
comes from native libraries, memory-mapped files or GPU staging buffers and you don't want to copy
it into managed arrays.

![Raw pointers case](../images/gallery-unsafe.png)
