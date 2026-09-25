// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet.Core;

namespace AplNet.Unsafe;

/// <summary>
/// <b>UNSAFE.</b> Parallel loops over raw pointers with function-pointer bodies: no delegates, no
/// bounds checks, no range validation. The "you asked for it" tier.
/// </summary>
/// <remarks>
/// <para>
/// <b>The caller guarantees</b> that every address the body touches stays valid for the whole call:
/// the memory must be pinned (<c>fixed</c>, <see cref="System.Runtime.InteropServices.GCHandle"/>,
/// <see cref="NativeBuffer{T}"/>, <c>NativeMemory</c>) and must not be freed until the method returns.
/// Nothing here checks <c>ptr + i</c> against anything; an off-by-one is memory corruption, not an
/// exception.
/// </para>
/// <para>
/// Bodies are <c>delegate*</c> function pointers, so they must be <c>static</c> methods (no closures).
/// Taking one with <c>&amp;Method</c> allocates nothing, and the call is a plain indirect call.
/// </para>
/// <para>
/// Scheduling is shared with the safe API: the same partitioners, options, pooled jobs and absence of
/// <see cref="ExecutionContext"/> flow. So is exception handling - a managed exception thrown by a
/// body is still collected into an <see cref="AggregateException"/> rather than tearing the process
/// down, because in .NET that costs nothing until something actually throws. An access violation is a
/// different matter: it cannot be caught and will terminate the process.
/// </para>
/// </remarks>
public static unsafe class UnsafeParallel
{
    /// <summary>
    /// Calls <c>body(ptr, i)</c> for every <c>i</c> in <c>[0, length)</c>, in parallel. The signature
    /// from the APL.Net specification; see <c>ForPtr&lt;T&gt;</c>
    /// for the generic form and <see cref="ForRangePtr{T}"/> for the faster range form.
    /// </summary>
    public static void ForPtr(double* ptr, int length, delegate*<double*, int, void> body, int? maxDegreeOfParallelism = null)
    {
        Validate(ptr, length, body);
        AplOptions? options = maxDegreeOfParallelism is null ? null : new AplOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism };
        ParallelExecutor.Run(0, length, new PointerElementBody<double>(ptr, body), options, defaultMinChunk: 1);
    }

    /// <summary>Calls <c>body(ptr, i)</c> for every <c>i</c> in <c>[0, length)</c>, in parallel.</summary>
    public static void ForPtr<T>(T* ptr, int length, delegate*<T*, int, void> body, AplOptions? options = null)
        where T : unmanaged
    {
        Validate(ptr, length, body);
        ParallelExecutor.Run(0, length, new PointerElementBody<T>(ptr, body), options, defaultMinChunk: 1);
    }

    /// <summary>
    /// Calls <c>body(ptr, from, to)</c> once per sub-range of <c>[0, length)</c>. One indirect call per
    /// range instead of per element, and the body is free to vectorize.
    /// </summary>
    public static void ForRangePtr<T>(T* ptr, int length, delegate*<T*, int, int, void> body, AplOptions? options = null)
        where T : unmanaged
    {
        Validate(ptr, length, body);
        ParallelExecutor.Run(0, length, new PointerRangeBody<T>(ptr, body), options, defaultMinChunk: 1);
    }

    /// <summary>
    /// Calls <c>body(context, from, to)</c> once per sub-range of <c>[fromInclusive, toExclusive)</c>.
    /// <paramref name="context"/> is passed through untouched - typically the address of an unmanaged
    /// struct holding several pointers and parameters, for bodies that need more than one buffer.
    /// </summary>
    public static void For(void* context, int fromInclusive, int toExclusive, delegate*<void*, int, int, void> body, AplOptions? options = null)
    {
        if (body is null)
            throw new ArgumentNullException(nameof(body));
        ParallelExecutor.Run(fromInclusive, toExclusive, new ContextRangeBody(context, body), options, defaultMinChunk: 1);
    }

    private static void Validate(void* ptr, int length, void* body)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (body is null)
            throw new ArgumentNullException(nameof(body));
        if (ptr is null && length > 0)
            throw new ArgumentNullException(nameof(ptr));
    }

    private readonly struct PointerElementBody<T>(T* ptr, delegate*<T*, int, void> body) : IRangeWorkBody
        where T : unmanaged
    {
        private readonly T* _ptr = ptr;
        private readonly delegate*<T*, int, void> _body = body;

        public void Invoke(int fromInclusive, int toExclusive)
        {
            T* ptr = _ptr;
            delegate*<T*, int, void> body = _body;
            for (int i = fromInclusive; i < toExclusive; i++)
                body(ptr, i);
        }
    }

    private readonly struct PointerRangeBody<T>(T* ptr, delegate*<T*, int, int, void> body) : IRangeWorkBody
        where T : unmanaged
    {
        private readonly T* _ptr = ptr;
        private readonly delegate*<T*, int, int, void> _body = body;

        public void Invoke(int fromInclusive, int toExclusive) => _body(_ptr, fromInclusive, toExclusive);
    }

    private readonly struct ContextRangeBody(void* context, delegate*<void*, int, int, void> body) : IRangeWorkBody
    {
        private readonly void* _context = context;
        private readonly delegate*<void*, int, int, void> _body = body;

        public void Invoke(int fromInclusive, int toExclusive) => _body(_context, fromInclusive, toExclusive);
    }
}
