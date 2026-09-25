// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace AplNet.Core;

/// <summary>
/// A loop body invoked once per index. Implement it on a <c>struct</c> (ideally a
/// <c>readonly struct</c>) and pass it by its concrete type.
/// </summary>
/// <remarks>
/// <para>
/// This is the mechanism that replaces delegate dispatch. APL.Net's loops are generic over
/// <c>TBody : struct, IWorkBody</c>; for a struct type argument the JIT compiles a dedicated copy of
/// the loop, so the call to <see cref="Invoke"/> is a direct call it can inline - no delegate, no
/// closure allocation, no interface dispatch.
/// </para>
/// <para>
/// Each worker invokes its own copy of the struct. Fields that are references (arrays, objects) are
/// shared; plain value fields are not, so mutating them does not communicate between workers.
/// </para>
/// </remarks>
public interface IWorkBody
{
    /// <summary>Runs one iteration.</summary>
    void Invoke(int index);
}

/// <summary>
/// A <c>ForEach</c> body that receives each element by reference, so it can read or replace it in
/// place without the copy a by-value delegate would make.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public interface IWorkBody<T>
{
    /// <summary>Runs one iteration over <paramref name="item"/>, the element at <paramref name="index"/>.</summary>
    void Invoke(int index, ref T item);
}

/// <summary>
/// A loop body invoked once per contiguous sub-range rather than once per index.
/// </summary>
/// <remarks>
/// The fastest shape APL.Net offers. Inside <see cref="Invoke"/> the body can slice arrays into spans
/// (the JIT then removes the bounds checks from a <c>for</c> loop over the span), hoist invariants
/// out of the loop, or hand the whole slice to a SIMD kernel. Ranges never overlap and together cover
/// the loop exactly once.
/// </remarks>
public interface IRangeWorkBody
{
    /// <summary>Runs every iteration in <c>[fromInclusive, toExclusive)</c>.</summary>
    void Invoke(int fromInclusive, int toExclusive);
}

/// <summary>
/// A parallel reduction: each worker folds its ranges into a private accumulator, and the
/// accumulators are then combined.
/// </summary>
/// <typeparam name="T">The accumulator type.</typeparam>
/// <remarks>
/// <see cref="Combine"/> must be associative, and the identity passed to the reduce call must be a
/// true identity for it (<c>Combine(identity, x) == x</c>): partitions are combined in a fixed order,
/// but how the range is split depends on the degree of parallelism. Floating-point sums therefore
/// match a sequential sum only up to rounding.
/// </remarks>
public interface IReduceBody<T>
{
    /// <summary>Folds every iteration in <c>[fromInclusive, toExclusive)</c> into <paramref name="accumulator"/>.</summary>
    T Accumulate(int fromInclusive, int toExclusive, T accumulator);

    /// <summary>Combines two partial results.</summary>
    T Combine(T left, T right);
}

/// <summary>A <c>ForEach</c> callback that receives the element by reference.</summary>
public delegate void RefItemAction<T>(int index, ref T item);
