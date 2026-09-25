// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.CompilerServices;

namespace AplNet.Core;

// Adapters from every public body shape to IRangeWorkBody, the one shape the executor runs.
// Each copies its inner body or delegate into a local before looping: a local can live in a
// register, and a non-readonly inner struct called through a readonly field would be copied
// defensively on every iteration.

internal struct ElementBody<TBody>(TBody body) : IRangeWorkBody
    where TBody : struct, IWorkBody
{
    private readonly TBody _body = body;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Invoke(int fromInclusive, int toExclusive)
    {
        TBody body = _body;
        for (int i = fromInclusive; i < toExclusive; i++)
            body.Invoke(i);
    }
}

internal readonly struct DelegateBody(Action<int> action) : IRangeWorkBody
{
    private readonly Action<int> _action = action;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke(int fromInclusive, int toExclusive)
    {
        Action<int> action = _action;
        for (int i = fromInclusive; i < toExclusive; i++)
            action(i);
    }
}

internal readonly struct RangeDelegateBody(Action<int, int> action) : IRangeWorkBody
{
    private readonly Action<int, int> _action = action;

    public void Invoke(int fromInclusive, int toExclusive) => _action(fromInclusive, toExclusive);
}

internal struct ArrayItemBody<T, TBody>(T[] array, TBody body) : IRangeWorkBody
    where TBody : struct, IWorkBody<T>
{
    private readonly T[] _array = array;
    private readonly TBody _body = body;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Invoke(int fromInclusive, int toExclusive)
    {
        // Looping over a span of exactly the range lets the JIT drop the bounds check on span[k].
        Span<T> span = new(_array, fromInclusive, toExclusive - fromInclusive);
        TBody body = _body;
        for (int k = 0; k < span.Length; k++)
            body.Invoke(fromInclusive + k, ref span[k]);
    }
}

internal struct MemoryItemBody<T, TBody>(Memory<T> memory, TBody body) : IRangeWorkBody
    where TBody : struct, IWorkBody<T>
{
    private readonly Memory<T> _memory = memory;
    private readonly TBody _body = body;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Invoke(int fromInclusive, int toExclusive)
    {
        Span<T> span = _memory.Span.Slice(fromInclusive, toExclusive - fromInclusive);
        TBody body = _body;
        for (int k = 0; k < span.Length; k++)
            body.Invoke(fromInclusive + k, ref span[k]);
    }
}

internal readonly struct ArrayValueDelegateBody<T>(T[] array, Action<T> action) : IRangeWorkBody
{
    private readonly T[] _array = array;
    private readonly Action<T> _action = action;

    public void Invoke(int fromInclusive, int toExclusive)
    {
        ReadOnlySpan<T> span = new(_array, fromInclusive, toExclusive - fromInclusive);
        Action<T> action = _action;
        for (int k = 0; k < span.Length; k++)
            action(span[k]);
    }
}

internal readonly struct ArrayRefDelegateBody<T>(T[] array, RefItemAction<T> action) : IRangeWorkBody
{
    private readonly T[] _array = array;
    private readonly RefItemAction<T> _action = action;

    public void Invoke(int fromInclusive, int toExclusive)
    {
        Span<T> span = new(_array, fromInclusive, toExclusive - fromInclusive);
        RefItemAction<T> action = _action;
        for (int k = 0; k < span.Length; k++)
            action(fromInclusive + k, ref span[k]);
    }
}

internal readonly struct DelegateReduceBody<T>(Func<int, int, T, T> accumulate, Func<T, T, T> combine) : IReduceBody<T>
{
    private readonly Func<int, int, T, T> _accumulate = accumulate;
    private readonly Func<T, T, T> _combine = combine;

    public T Accumulate(int fromInclusive, int toExclusive, T accumulator) => _accumulate(fromInclusive, toExclusive, accumulator);

    public T Combine(T left, T right) => _combine(left, right);
}
