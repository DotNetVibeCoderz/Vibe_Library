// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Numerics;
using System.Runtime.Intrinsics;
using AplNet.Core;

namespace AplNet.Simd;

/// <summary>
/// SIMD bulk operations - map, map-reduce and in-place transform - single-threaded over spans and
/// parallel over arrays or <see cref="Memory{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Operations are expressed with operator structs (<see cref="IUnaryOperator{T}"/>,
/// <see cref="IBinaryOperator{T}"/>, <see cref="IReduceOperator{T}"/>); the kernels are generic over
/// them, so every operator call is inlined. Built-in operators cover the common cases
/// (<see cref="MultiplyAddOperator{T}"/>, <see cref="AddOperator{T}"/>, <see cref="MinOperator{T}"/>,
/// ...). Supported element types are the primitive numerics the .NET vector types support: at least
/// <c>float</c>, <c>double</c>, <c>int</c> and <c>long</c>, plus the other integer widths.
/// </para>
/// <para>
/// The parallel methods take arrays or <see cref="Memory{T}"/> rather than spans because a span cannot
/// be handed to another thread; each worker turns its slice back into a span and runs the same
/// single-threaded kernel over it. They split no finer than <see cref="DefaultMinChunkSize"/> elements
/// per worker unless <see cref="AplOptions.MinChunkSize"/> says otherwise: a vector loop gets through
/// 32K floats in a few microseconds, about what it costs to wake a pool thread.
/// </para>
/// </remarks>
public static class SimdOps
{
    /// <summary>The fewest elements a parallel SIMD operation gives one worker, unless the options override it.</summary>
    public const int DefaultMinChunkSize = 32 * 1024;

    // ---------------------------------------------------------------- single-threaded

    /// <summary><c>data[i] = op(data[i])</c>, vectorized.</summary>
    public static void TransformInPlace<T, TOp>(Span<T> data, TOp op)
        where T : struct
        where TOp : struct, IUnaryOperator<T> =>
        SimdKernels.Transform<T, TOp>(data, data, op);

    /// <summary><c>destination[i] = op(source[i])</c>, vectorized. <paramref name="destination"/> may be the same memory as <paramref name="source"/>, but must not partially overlap it.</summary>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <paramref name="source"/>, or they partially overlap.</exception>
    public static void Transform<T, TOp>(ReadOnlySpan<T> source, Span<T> destination, TOp op)
        where T : struct
        where TOp : struct, IUnaryOperator<T>
    {
        ValidateDestination(source, destination);
        SimdKernels.Transform(source, destination[..source.Length], op);
    }

    /// <summary><c>destination[i] = op(x[i], y[i])</c>, vectorized.</summary>
    /// <exception cref="ArgumentException">The inputs differ in length, <paramref name="destination"/> is too short, or it partially overlaps an input.</exception>
    public static void Transform<T, TOp>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, Span<T> destination, TOp op)
        where T : struct
        where TOp : struct, IBinaryOperator<T>
    {
        ValidateSameLength(x, y);
        ValidateDestination(x, destination);
        ValidateDestination(y, destination);
        SimdKernels.Transform(x, y, destination[..x.Length], op);
    }

    /// <summary>
    /// <c>data[i] = op(data[i])</c> with the operation given as delegates: <paramref name="vectorOp"/>
    /// for each group of <see cref="Vector256{T}.Count"/> elements, <paramref name="scalarOp"/> for the
    /// rest - and for everything when the CPU has no 256-bit vectors. Costs a delegate call per vector;
    /// an operator struct costs nothing.
    /// </summary>
    public static void TransformInPlace<T>(Span<T> data, Func<Vector256<T>, Vector256<T>> vectorOp, Func<T, T> scalarOp)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(vectorOp);
        ArgumentNullException.ThrowIfNull(scalarOp);
        SimdKernels.TransformWithDelegates(data, vectorOp, scalarOp);
    }

    /// <summary>Folds <paramref name="source"/> with <paramref name="op"/>; returns its identity when empty.</summary>
    public static T Reduce<T, TOp>(ReadOnlySpan<T> source, TOp op)
        where T : struct
        where TOp : struct, IReduceOperator<T> =>
        SimdKernels.Reduce(source, default(IdentityOperator<T>), op);

    /// <summary><c>reduce(map(source[0]), map(source[1]), ...)</c>, e.g. a sum of squares.</summary>
    public static T MapReduce<T, TMap, TReduce>(ReadOnlySpan<T> source, TMap map, TReduce reduce)
        where T : struct
        where TMap : struct, IUnaryOperator<T>
        where TReduce : struct, IReduceOperator<T> =>
        SimdKernels.Reduce(source, map, reduce);

    /// <summary><c>reduce(map(x[0], y[0]), map(x[1], y[1]), ...)</c>, e.g. a dot product.</summary>
    /// <exception cref="ArgumentException">The inputs differ in length.</exception>
    public static T MapReduce<T, TMap, TReduce>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, TMap map, TReduce reduce)
        where T : struct
        where TMap : struct, IBinaryOperator<T>
        where TReduce : struct, IReduceOperator<T>
    {
        ValidateSameLength(x, y);
        return SimdKernels.Reduce(x, y, map, reduce);
    }

    /// <summary>The sum of <paramref name="source"/>; zero when empty.</summary>
    public static T Sum<T>(ReadOnlySpan<T> source)
        where T : unmanaged, INumber<T> =>
        SimdKernels.Reduce(source, default(IdentityOperator<T>), default(AddOperator<T>));

    /// <summary>The sum of the squares of <paramref name="source"/>.</summary>
    public static T SumOfSquares<T>(ReadOnlySpan<T> source)
        where T : unmanaged, INumber<T> =>
        SimdKernels.Reduce(source, default(SquareOperator<T>), default(AddOperator<T>));

    /// <summary>The smallest element.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="source"/> is empty.</exception>
    public static T Min<T>(ReadOnlySpan<T> source)
        where T : unmanaged, INumber<T>
    {
        ThrowIfEmpty(source.Length);
        return SimdKernels.Reduce(source, default(IdentityOperator<T>), default(MinOperator<T>));
    }

    /// <summary>The largest element.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="source"/> is empty.</exception>
    public static T Max<T>(ReadOnlySpan<T> source)
        where T : unmanaged, INumber<T>
    {
        ThrowIfEmpty(source.Length);
        return SimdKernels.Reduce(source, default(IdentityOperator<T>), default(MaxOperator<T>));
    }

    /// <summary>The dot product <c>x[0]*y[0] + x[1]*y[1] + ...</c>.</summary>
    /// <exception cref="ArgumentException">The inputs differ in length.</exception>
    public static T Dot<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y)
        where T : unmanaged, INumber<T>
    {
        ValidateSameLength(x, y);
        return SimdKernels.Reduce(x, y, default(MultiplyOperator<T>), default(AddOperator<T>));
    }

    // ---------------------------------------------------------------- parallel: transforms

    /// <summary><c>data[i] = op(data[i])</c> across all workers, vectorized within each.</summary>
    public static void ParallelTransformInPlace<T, TOp>(T[] data, TOp op, AplOptions? options = null)
        where T : struct
        where TOp : struct, IUnaryOperator<T>
    {
        ArgumentNullException.ThrowIfNull(data);
        ParallelTransformInPlace(data.AsMemory(), op, options);
    }

    /// <summary><c>data[i] = op(data[i])</c> across all workers, vectorized within each.</summary>
    public static void ParallelTransformInPlace<T, TOp>(Memory<T> data, TOp op, AplOptions? options = null)
        where T : struct
        where TOp : struct, IUnaryOperator<T> =>
        ParallelExecutor.Run(0, data.Length, new UnaryBody<T, TOp>(data, data, op), options, DefaultMinChunkSize);

    /// <summary>The parallel form of <see cref="TransformInPlace{T}(Span{T}, Func{Vector256{T}, Vector256{T}}, Func{T, T})"/>.</summary>
    public static void ParallelTransformInPlace<T>(T[] data, Func<Vector256<T>, Vector256<T>> vectorOp, Func<T, T> scalarOp, AplOptions? options = null)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        ParallelTransformInPlace(data.AsMemory(), vectorOp, scalarOp, options);
    }

    /// <summary>The parallel form of <see cref="TransformInPlace{T}(Span{T}, Func{Vector256{T}, Vector256{T}}, Func{T, T})"/>.</summary>
    public static void ParallelTransformInPlace<T>(Memory<T> data, Func<Vector256<T>, Vector256<T>> vectorOp, Func<T, T> scalarOp, AplOptions? options = null)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(vectorOp);
        ArgumentNullException.ThrowIfNull(scalarOp);
        ParallelExecutor.Run(0, data.Length, new DelegateTransformBody<T>(data, vectorOp, scalarOp), options, DefaultMinChunkSize);
    }

    /// <summary><c>destination[i] = op(source[i])</c> across all workers, vectorized within each.</summary>
    public static void ParallelTransform<T, TOp>(T[] source, T[] destination, TOp op, AplOptions? options = null)
        where T : struct
        where TOp : struct, IUnaryOperator<T>
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ParallelTransform(new ReadOnlyMemory<T>(source), destination.AsMemory(), op, options);
    }

    /// <summary><c>destination[i] = op(source[i])</c> across all workers, vectorized within each.</summary>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <paramref name="source"/>, or they partially overlap.</exception>
    public static void ParallelTransform<T, TOp>(ReadOnlyMemory<T> source, Memory<T> destination, TOp op, AplOptions? options = null)
        where T : struct
        where TOp : struct, IUnaryOperator<T>
    {
        ValidateDestination(source.Span, destination.Span);
        ParallelExecutor.Run(0, source.Length, new UnaryBody<T, TOp>(source, destination, op), options, DefaultMinChunkSize);
    }

    /// <summary><c>destination[i] = op(x[i], y[i])</c> across all workers, vectorized within each.</summary>
    public static void ParallelTransform<T, TOp>(T[] x, T[] y, T[] destination, TOp op, AplOptions? options = null)
        where T : struct
        where TOp : struct, IBinaryOperator<T>
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(destination);
        ParallelTransform(new ReadOnlyMemory<T>(x), new ReadOnlyMemory<T>(y), destination.AsMemory(), op, options);
    }

    /// <summary><c>destination[i] = op(x[i], y[i])</c> across all workers, vectorized within each.</summary>
    /// <exception cref="ArgumentException">The inputs differ in length, <paramref name="destination"/> is too short, or it partially overlaps an input.</exception>
    public static void ParallelTransform<T, TOp>(ReadOnlyMemory<T> x, ReadOnlyMemory<T> y, Memory<T> destination, TOp op, AplOptions? options = null)
        where T : struct
        where TOp : struct, IBinaryOperator<T>
    {
        ValidateSameLength(x.Span, y.Span);
        ValidateDestination(x.Span, destination.Span);
        ValidateDestination(y.Span, destination.Span);
        ParallelExecutor.Run(0, x.Length, new BinaryBody<T, TOp>(x, y, destination, op), options, DefaultMinChunkSize);
    }

    // ---------------------------------------------------------------- parallel: reductions

    /// <summary>Folds <paramref name="source"/> with <paramref name="op"/> across all workers; returns its identity when empty.</summary>
    public static T ParallelReduce<T, TOp>(T[] source, TOp op, AplOptions? options = null)
        where T : struct
        where TOp : struct, IReduceOperator<T>
    {
        ArgumentNullException.ThrowIfNull(source);
        return ParallelMapReduce(new ReadOnlyMemory<T>(source), default(IdentityOperator<T>), op, options);
    }

    /// <summary>Folds <paramref name="source"/> with <paramref name="op"/> across all workers; returns its identity when empty.</summary>
    public static T ParallelReduce<T, TOp>(ReadOnlyMemory<T> source, TOp op, AplOptions? options = null)
        where T : struct
        where TOp : struct, IReduceOperator<T> =>
        ParallelMapReduce(source, default(IdentityOperator<T>), op, options);

    /// <summary>The parallel form of <see cref="MapReduce{T, TMap, TReduce}(ReadOnlySpan{T}, TMap, TReduce)"/>.</summary>
    public static T ParallelMapReduce<T, TMap, TReduce>(T[] source, TMap map, TReduce reduce, AplOptions? options = null)
        where T : struct
        where TMap : struct, IUnaryOperator<T>
        where TReduce : struct, IReduceOperator<T>
    {
        ArgumentNullException.ThrowIfNull(source);
        return ParallelMapReduce(new ReadOnlyMemory<T>(source), map, reduce, options);
    }

    /// <summary>The parallel form of <see cref="MapReduce{T, TMap, TReduce}(ReadOnlySpan{T}, TMap, TReduce)"/>.</summary>
    public static T ParallelMapReduce<T, TMap, TReduce>(ReadOnlyMemory<T> source, TMap map, TReduce reduce, AplOptions? options = null)
        where T : struct
        where TMap : struct, IUnaryOperator<T>
        where TReduce : struct, IReduceOperator<T> =>
        ParallelExecutor.RunReduce(0, source.Length, reduce.Identity, new UnaryReduceBody<T, TMap, TReduce>(source, map, reduce), options, DefaultMinChunkSize);

    /// <summary>The parallel form of <see cref="MapReduce{T, TMap, TReduce}(ReadOnlySpan{T}, ReadOnlySpan{T}, TMap, TReduce)"/>.</summary>
    public static T ParallelMapReduce<T, TMap, TReduce>(T[] x, T[] y, TMap map, TReduce reduce, AplOptions? options = null)
        where T : struct
        where TMap : struct, IBinaryOperator<T>
        where TReduce : struct, IReduceOperator<T>
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        return ParallelMapReduce(new ReadOnlyMemory<T>(x), new ReadOnlyMemory<T>(y), map, reduce, options);
    }

    /// <summary>The parallel form of <see cref="MapReduce{T, TMap, TReduce}(ReadOnlySpan{T}, ReadOnlySpan{T}, TMap, TReduce)"/>.</summary>
    /// <exception cref="ArgumentException">The inputs differ in length.</exception>
    public static T ParallelMapReduce<T, TMap, TReduce>(ReadOnlyMemory<T> x, ReadOnlyMemory<T> y, TMap map, TReduce reduce, AplOptions? options = null)
        where T : struct
        where TMap : struct, IBinaryOperator<T>
        where TReduce : struct, IReduceOperator<T>
    {
        ValidateSameLength(x.Span, y.Span);
        return ParallelExecutor.RunReduce(0, x.Length, reduce.Identity, new BinaryReduceBody<T, TMap, TReduce>(x, y, map, reduce), options, DefaultMinChunkSize);
    }

    /// <summary>The sum of <paramref name="source"/>, parallel and vectorized; zero when empty.</summary>
    public static T ParallelSum<T>(T[] source, AplOptions? options = null)
        where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(source);
        return ParallelReduce(new ReadOnlyMemory<T>(source), default(AddOperator<T>), options);
    }

    /// <summary>The sum of <paramref name="source"/>, parallel and vectorized; zero when empty.</summary>
    public static T ParallelSum<T>(ReadOnlyMemory<T> source, AplOptions? options = null)
        where T : unmanaged, INumber<T> =>
        ParallelReduce(source, default(AddOperator<T>), options);

    /// <summary>The smallest element, parallel and vectorized.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="source"/> is empty.</exception>
    public static T ParallelMin<T>(T[] source, AplOptions? options = null)
        where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(source);
        return ParallelMin(new ReadOnlyMemory<T>(source), options);
    }

    /// <summary>The smallest element, parallel and vectorized.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="source"/> is empty.</exception>
    public static T ParallelMin<T>(ReadOnlyMemory<T> source, AplOptions? options = null)
        where T : unmanaged, INumber<T>
    {
        ThrowIfEmpty(source.Length);
        return ParallelReduce(source, default(MinOperator<T>), options);
    }

    /// <summary>The largest element, parallel and vectorized.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="source"/> is empty.</exception>
    public static T ParallelMax<T>(T[] source, AplOptions? options = null)
        where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(source);
        return ParallelMax(new ReadOnlyMemory<T>(source), options);
    }

    /// <summary>The largest element, parallel and vectorized.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="source"/> is empty.</exception>
    public static T ParallelMax<T>(ReadOnlyMemory<T> source, AplOptions? options = null)
        where T : unmanaged, INumber<T>
    {
        ThrowIfEmpty(source.Length);
        return ParallelReduce(source, default(MaxOperator<T>), options);
    }

    /// <summary>The dot product, parallel and vectorized.</summary>
    public static T ParallelDot<T>(T[] x, T[] y, AplOptions? options = null)
        where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        return ParallelDot(new ReadOnlyMemory<T>(x), new ReadOnlyMemory<T>(y), options);
    }

    /// <summary>The dot product, parallel and vectorized.</summary>
    /// <exception cref="ArgumentException">The inputs differ in length.</exception>
    public static T ParallelDot<T>(ReadOnlyMemory<T> x, ReadOnlyMemory<T> y, AplOptions? options = null)
        where T : unmanaged, INumber<T> =>
        ParallelMapReduce(x, y, default(MultiplyOperator<T>), default(AddOperator<T>), options);

    // ---------------------------------------------------------------- validation

    private static void ValidateDestination<T>(ReadOnlySpan<T> source, ReadOnlySpan<T> destination)
    {
        if (destination.Length < source.Length)
            throw new ArgumentException("Destination is shorter than the source.", nameof(destination));
        if (source.Overlaps(destination, out int offset) && offset != 0)
            throw new ArgumentException("Destination partially overlaps the source; it must be the same memory or separate memory.", nameof(destination));
    }

    private static void ValidateSameLength<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y)
    {
        if (x.Length != y.Length)
            throw new ArgumentException($"Inputs must have the same length ({x.Length} vs {y.Length}).", nameof(y));
    }

    private static void ThrowIfEmpty(int length)
    {
        if (length == 0)
            throw new InvalidOperationException("Sequence contains no elements.");
    }

    // ---------------------------------------------------------------- per-chunk bodies

    private readonly struct UnaryBody<T, TOp>(ReadOnlyMemory<T> source, Memory<T> destination, TOp op) : IRangeWorkBody
        where T : struct
        where TOp : struct, IUnaryOperator<T>
    {
        private readonly ReadOnlyMemory<T> _source = source;
        private readonly Memory<T> _destination = destination;
        private readonly TOp _op = op;

        public void Invoke(int fromInclusive, int toExclusive)
        {
            int length = toExclusive - fromInclusive;
            SimdKernels.Transform(_source.Span.Slice(fromInclusive, length), _destination.Span.Slice(fromInclusive, length), _op);
        }
    }

    private readonly struct BinaryBody<T, TOp>(ReadOnlyMemory<T> x, ReadOnlyMemory<T> y, Memory<T> destination, TOp op) : IRangeWorkBody
        where T : struct
        where TOp : struct, IBinaryOperator<T>
    {
        private readonly ReadOnlyMemory<T> _x = x;
        private readonly ReadOnlyMemory<T> _y = y;
        private readonly Memory<T> _destination = destination;
        private readonly TOp _op = op;

        public void Invoke(int fromInclusive, int toExclusive)
        {
            int length = toExclusive - fromInclusive;
            SimdKernels.Transform(_x.Span.Slice(fromInclusive, length), _y.Span.Slice(fromInclusive, length), _destination.Span.Slice(fromInclusive, length), _op);
        }
    }

    private readonly struct DelegateTransformBody<T>(Memory<T> data, Func<Vector256<T>, Vector256<T>> vectorOp, Func<T, T> scalarOp) : IRangeWorkBody
        where T : struct
    {
        private readonly Memory<T> _data = data;
        private readonly Func<Vector256<T>, Vector256<T>> _vectorOp = vectorOp;
        private readonly Func<T, T> _scalarOp = scalarOp;

        public void Invoke(int fromInclusive, int toExclusive) =>
            SimdKernels.TransformWithDelegates(_data.Span.Slice(fromInclusive, toExclusive - fromInclusive), _vectorOp, _scalarOp);
    }

    private readonly struct UnaryReduceBody<T, TMap, TReduce>(ReadOnlyMemory<T> source, TMap map, TReduce reduce) : IReduceBody<T>
        where T : struct
        where TMap : struct, IUnaryOperator<T>
        where TReduce : struct, IReduceOperator<T>
    {
        private readonly ReadOnlyMemory<T> _source = source;
        private readonly TMap _map = map;
        private readonly TReduce _reduce = reduce;

        public T Accumulate(int fromInclusive, int toExclusive, T accumulator) =>
            _reduce.Invoke(accumulator, SimdKernels.Reduce(_source.Span.Slice(fromInclusive, toExclusive - fromInclusive), _map, _reduce));

        public T Combine(T left, T right) => _reduce.Invoke(left, right);
    }

    private readonly struct BinaryReduceBody<T, TMap, TReduce>(ReadOnlyMemory<T> x, ReadOnlyMemory<T> y, TMap map, TReduce reduce) : IReduceBody<T>
        where T : struct
        where TMap : struct, IBinaryOperator<T>
        where TReduce : struct, IReduceOperator<T>
    {
        private readonly ReadOnlyMemory<T> _x = x;
        private readonly ReadOnlyMemory<T> _y = y;
        private readonly TMap _map = map;
        private readonly TReduce _reduce = reduce;

        public T Accumulate(int fromInclusive, int toExclusive, T accumulator)
        {
            int length = toExclusive - fromInclusive;
            return _reduce.Invoke(accumulator, SimdKernels.Reduce(_x.Span.Slice(fromInclusive, length), _y.Span.Slice(fromInclusive, length), _map, _reduce));
        }

        public T Combine(T left, T right) => _reduce.Invoke(left, right);
    }
}
