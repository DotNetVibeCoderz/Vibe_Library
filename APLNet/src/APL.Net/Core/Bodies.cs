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

// ---------------------------------------------------------------- 64-bit ranges

/// <summary>
/// Runs a 64-bit range as an <c>int</c> one: unit <c>u</c> of the executor's range covers iterations
/// <c>[from + u * scale, from + (u + 1) * scale)</c>, clipped to the count. <c>scale</c> is 1
/// whenever the count fits in an <c>int</c>, so units map one to one onto iterations; beyond that it
/// is the smallest factor that brings the count under <see cref="int.MaxValue"/> units.
/// </summary>
internal readonly struct LongRangeBody<TBody>(TBody body, long from, ulong count, ulong scale, int units) : IRangeWorkBody
    where TBody : struct, ILongRangeWorkBody
{
    private readonly TBody _body = body;
    private readonly long _from = from;
    private readonly ulong _count = count;
    private readonly ulong _scale = scale;
    private readonly int _units = units;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke(int fromInclusive, int toExclusive)
    {
        // The last unit is clipped to the count. Computing units * scale there could pass 2^64.
        ulong start = (ulong)fromInclusive * _scale;
        ulong end = toExclusive == _units ? _count : (ulong)toExclusive * _scale;
        TBody body = _body;
        body.Invoke(unchecked(_from + (long)start), unchecked(_from + (long)end));
    }
}

internal readonly struct LongElementBody<TBody>(TBody body) : ILongRangeWorkBody
    where TBody : struct, ILongWorkBody
{
    private readonly TBody _body = body;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke(long fromInclusive, long toExclusive)
    {
        TBody body = _body;
        for (long i = fromInclusive; i < toExclusive; i++)
            body.Invoke(i);
    }
}

internal readonly struct LongDelegateBody(Action<long> action) : ILongRangeWorkBody
{
    private readonly Action<long> _action = action;

    public void Invoke(long fromInclusive, long toExclusive)
    {
        Action<long> action = _action;
        for (long i = fromInclusive; i < toExclusive; i++)
            action(i);
    }
}

internal readonly struct LongRangeDelegateBody(Action<long, long> action) : ILongRangeWorkBody
{
    private readonly Action<long, long> _action = action;

    public void Invoke(long fromInclusive, long toExclusive) => _action(fromInclusive, toExclusive);
}

// ---------------------------------------------------------------- loop state

/// <summary>Checks the shared limit before every iteration. Ranges ascend, so the first index past it ends the range.</summary>
internal readonly struct StatefulElementBody<TBody>(TBody body, LoopStateCore core) : IRangeWorkBody
    where TBody : struct, IStatefulWorkBody
{
    private readonly TBody _body = body;
    private readonly LoopStateCore _core = core;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke(int fromInclusive, int toExclusive)
    {
        TBody body = _body;
        LoopStateCore core = _core;
        for (int i = fromInclusive; i < toExclusive; i++)
        {
            if (core.ShouldExit(i))
                return;
            body.Invoke(i, new AplLoopState(core, i));
        }
    }
}

internal readonly struct StatefulDelegateBody(Action<int, AplLoopState> action) : IStatefulWorkBody
{
    private readonly Action<int, AplLoopState> _action = action;

    public void Invoke(int index, AplLoopState state) => _action(index, state);
}
