# NativeBuffer&lt;T&gt; class

Namespace: `AplNet.Unsafe` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public sealed class NativeBuffer<T> : IDisposable
    where T : unmanaged
```

A fixed-size buffer of `T` in native memory, aligned to 64 bytes (one cache line, and one AVX-512 vector). Never moved by the GC, so its [`NativeBuffer<T>.Pointer`](AplNet.Unsafe.NativeBuffer-1.md) can be handed to [`UnsafeParallel`](AplNet.Unsafe.UnsafeParallel.md) without pinning, and it adds nothing to the managed heap.

**Remarks**

Dispose it when done; a finalizer frees the memory if you forget, but only whenever the GC gets to it. Using the buffer after disposal is undefined behaviour, as with any native allocation - [`NativeBuffer<T>.Span`](AplNet.Unsafe.NativeBuffer-1.md) and [`NativeBuffer<T>.Memory`](AplNet.Unsafe.NativeBuffer-1.md) throw `ObjectDisposedException`, but a pointer taken earlier does not.

## Constructors

### NativeBuffer(int, bool)

```csharp
public NativeBuffer(int length, bool clear = true);
```

Allocates `length` elements, zeroed if `clear` is true.

## Fields

### Alignment

```csharp
public const int Alignment = 64;
```

The alignment of [`NativeBuffer<T>.Pointer`](AplNet.Unsafe.NativeBuffer-1.md), in bytes.

## Properties

### Item

```csharp
public T Item { get; }
```

### Length

```csharp
public int Length { get; }
```

The number of elements.

### Memory

```csharp
public Memory<T> Memory { get; }
```

The buffer as `Memory<T>`, for the safe parallel APIs (which cannot take spans). Creates one small wrapper object on first use.

**Exceptions**

- `ObjectDisposedException`: The buffer has been disposed.

### Pointer

```csharp
public T* Pointer { get; }
```

The address of the first element.

**Exceptions**

- `ObjectDisposedException`: The buffer has been disposed.

### Span

```csharp
public Span<T> Span { get; }
```

The buffer as a span.

**Exceptions**

- `ObjectDisposedException`: The buffer has been disposed.

## Methods

### Dispose()

```csharp
public void Dispose();
```

Frees the memory.
