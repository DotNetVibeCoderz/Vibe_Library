// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers;
using System.Runtime.InteropServices;

namespace AplNet.Unsafe;

/// <summary>
/// A fixed-size buffer of <typeparamref name="T"/> in native memory, aligned to 64 bytes (one cache
/// line, and one AVX-512 vector). Never moved by the GC, so its <see cref="Pointer"/> can be handed to
/// <see cref="UnsafeParallel"/> without pinning, and it adds nothing to the managed heap.
/// </summary>
/// <remarks>
/// Dispose it when done; a finalizer frees the memory if you forget, but only whenever the GC gets to
/// it. Using the buffer after disposal is undefined behaviour, as with any native allocation -
/// <see cref="Span"/> and <see cref="Memory"/> throw <see cref="ObjectDisposedException"/>, but a
/// pointer taken earlier does not.
/// </remarks>
public sealed unsafe class NativeBuffer<T> : IDisposable
    where T : unmanaged
{
    /// <summary>The alignment of <see cref="Pointer"/>, in bytes.</summary>
    public const int Alignment = 64;

    private T* _pointer;
    private BufferMemoryManager? _memoryManager;

    /// <summary>Allocates <paramref name="length"/> elements, zeroed if <paramref name="clear"/> is true.</summary>
    public NativeBuffer(int length, bool clear = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Length = length;
        nuint bytes = (nuint)length * (nuint)sizeof(T);
        _pointer = (T*)NativeMemory.AlignedAlloc(Math.Max(bytes, 1), Alignment);
        if (clear)
            NativeMemory.Clear(_pointer, bytes);
    }

    /// <summary>Frees the memory if <see cref="Dispose"/> was never called.</summary>
    ~NativeBuffer() => Free();

    /// <summary>The number of elements.</summary>
    public int Length { get; }

    /// <summary>The address of the first element.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public T* Pointer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_pointer is null, this);
            return _pointer;
        }
    }

    /// <summary>The buffer as a span.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public Span<T> Span => new(Pointer, Length);

    /// <summary>
    /// The buffer as <see cref="Memory{T}"/>, for the safe parallel APIs (which cannot take spans).
    /// Creates one small wrapper object on first use.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public Memory<T> Memory
    {
        get
        {
            ObjectDisposedException.ThrowIf(_pointer is null, this);
            return (_memoryManager ??= new BufferMemoryManager(this)).Memory;
        }
    }

    /// <summary>A reference to element <paramref name="index"/>, bounds checked.</summary>
    public ref T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Length, nameof(index));
            return ref Pointer[index];
        }
    }

    /// <summary>Frees the memory.</summary>
    public void Dispose()
    {
        Free();
        GC.SuppressFinalize(this);
    }

    private void Free()
    {
        T* pointer = _pointer;
        _pointer = null;
        if (pointer is not null)
            NativeMemory.AlignedFree(pointer);
    }

    private sealed class BufferMemoryManager(NativeBuffer<T> owner) : MemoryManager<T>
    {
        public override Span<T> GetSpan() => owner.Span;

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)elementIndex, (uint)owner.Length, nameof(elementIndex));
            return new MemoryHandle(owner.Pointer + elementIndex);
        }

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
