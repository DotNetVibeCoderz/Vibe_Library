// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace AplNet.Simd;

// Operators are structs so that the kernels, which are generic over them, get a specialised copy
// per operator with every Invoke inlined. Each operator states its operation once per vector width;
// the kernel picks the widest width the hardware accelerates and uses the scalar form for the tail.
// Default interface methods would have saved some typing but are called through a boxed copy when the
// implementer is a struct, which is exactly the cost these interfaces exist to avoid.

/// <summary>An elementwise function <c>f(x)</c>, stated for scalars and for each vector width.</summary>
/// <typeparam name="T">A primitive numeric type (<c>float</c>, <c>double</c>, <c>int</c>, <c>long</c>, ...).</typeparam>
public interface IUnaryOperator<T>
    where T : struct
{
    /// <summary>Applies the function to one value.</summary>
    T Invoke(T x);

    /// <summary>Applies the function to every lane.</summary>
    Vector128<T> Invoke(Vector128<T> x);

    /// <summary>Applies the function to every lane.</summary>
    Vector256<T> Invoke(Vector256<T> x);

    /// <summary>Applies the function to every lane.</summary>
    Vector512<T> Invoke(Vector512<T> x);
}

/// <summary>An elementwise function <c>f(x, y)</c>, stated for scalars and for each vector width.</summary>
/// <typeparam name="T">A primitive numeric type.</typeparam>
public interface IBinaryOperator<T>
    where T : struct
{
    /// <summary>Applies the function to one pair.</summary>
    T Invoke(T x, T y);

    /// <summary>Applies the function lane by lane.</summary>
    Vector128<T> Invoke(Vector128<T> x, Vector128<T> y);

    /// <summary>Applies the function lane by lane.</summary>
    Vector256<T> Invoke(Vector256<T> x, Vector256<T> y);

    /// <summary>Applies the function lane by lane.</summary>
    Vector512<T> Invoke(Vector512<T> x, Vector512<T> y);
}

/// <summary>
/// A monoid for reductions: an associative (and, for parallel use, commutative) binary operation with
/// an identity, plus the horizontal reduction of one vector to a scalar. Sum, min and max are built in;
/// implement this for a custom one.
/// </summary>
/// <typeparam name="T">A primitive numeric type.</typeparam>
public interface IReduceOperator<T> : IBinaryOperator<T>
    where T : struct
{
    /// <summary>The identity: <c>Invoke(Identity, x) == x</c> for every <c>x</c>.</summary>
    T Identity { get; }

    /// <summary>Combines every lane of <paramref name="x"/> into one value.</summary>
    T Reduce(Vector128<T> x);

    /// <summary>Combines every lane of <paramref name="x"/> into one value.</summary>
    T Reduce(Vector256<T> x);

    /// <summary>Combines every lane of <paramref name="x"/> into one value.</summary>
    T Reduce(Vector512<T> x);
}

// ---------------------------------------------------------------- unary

/// <summary><c>x</c>. Used as the "map" of a plain reduction; the JIT inlines it away.</summary>
public readonly struct IdentityOperator<T> : IUnaryOperator<T>
    where T : struct
{
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x) => x;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector128<T> Invoke(Vector128<T> x) => x;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector256<T> Invoke(Vector256<T> x) => x;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector512<T> Invoke(Vector512<T> x) => x;
}

/// <summary><c>-x</c>.</summary>
public readonly struct NegateOperator<T> : IUnaryOperator<T>
    where T : unmanaged, INumber<T>
{
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x) => -x;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector128<T> Invoke(Vector128<T> x) => -x;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector256<T> Invoke(Vector256<T> x) => -x;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector512<T> Invoke(Vector512<T> x) => -x;
}

/// <summary><c>|x|</c>.</summary>
public readonly struct AbsOperator<T> : IUnaryOperator<T>
    where T : unmanaged, INumber<T>
{
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x) => T.Abs(x);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector128<T> Invoke(Vector128<T> x) => Vector128.Abs(x);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector256<T> Invoke(Vector256<T> x) => Vector256.Abs(x);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector512<T> Invoke(Vector512<T> x) => Vector512.Abs(x);
}

/// <summary><c>x * x</c>.</summary>
public readonly struct SquareOperator<T> : IUnaryOperator<T>
    where T : unmanaged, INumber<T>
{
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x) => x * x;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector128<T> Invoke(Vector128<T> x) => x * x;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector256<T> Invoke(Vector256<T> x) => x * x;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector512<T> Invoke(Vector512<T> x) => x * x;
}

/// <summary><c>sqrt(x)</c>, for <c>float</c> and <c>double</c>.</summary>
public readonly struct SqrtOperator<T> : IUnaryOperator<T>
    where T : unmanaged, INumber<T>, IRootFunctions<T>
{
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x) => T.Sqrt(x);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector128<T> Invoke(Vector128<T> x) => Vector128.Sqrt(x);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector256<T> Invoke(Vector256<T> x) => Vector256.Sqrt(x);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector512<T> Invoke(Vector512<T> x) => Vector512.Sqrt(x);
}

/// <summary><c>x * factor</c>.</summary>
public readonly struct ScaleOperator<T>(T factor) : IUnaryOperator<T>
    where T : unmanaged, INumber<T>
{
    /// <summary>The multiplier.</summary>
    public T Factor { get; } = factor;

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x) => x * Factor;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector128<T> Invoke(Vector128<T> x) => x * Vector128.Create(Factor);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector256<T> Invoke(Vector256<T> x) => x * Vector256.Create(Factor);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector512<T> Invoke(Vector512<T> x) => x * Vector512.Create(Factor);
}

/// <summary><c>x + addend</c>.</summary>
public readonly struct AddScalarOperator<T>(T addend) : IUnaryOperator<T>
    where T : unmanaged, INumber<T>
{
    /// <summary>The value added to every element.</summary>
    public T Addend { get; } = addend;

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x) => x + Addend;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector128<T> Invoke(Vector128<T> x) => x + Vector128.Create(Addend);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector256<T> Invoke(Vector256<T> x) => x + Vector256.Create(Addend);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector512<T> Invoke(Vector512<T> x) => x + Vector512.Create(Addend);
}

/// <summary>
/// <c>x * multiplier + addend</c>: the AXPY-style affine map. Computed as a multiply then an add, so
/// results match the scalar expression bit for bit (a fused multiply-add would round once instead of twice).
/// </summary>
public readonly struct MultiplyAddOperator<T>(T multiplier, T addend) : IUnaryOperator<T>
    where T : unmanaged, INumber<T>
{
    /// <summary>The multiplier <c>a</c> in <c>x * a + b</c>.</summary>
    public T Multiplier { get; } = multiplier;

    /// <summary>The addend <c>b</c> in <c>x * a + b</c>.</summary>
    public T Addend { get; } = addend;

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x) => x * Multiplier + Addend;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector128<T> Invoke(Vector128<T> x) => x * Vector128.Create(Multiplier) + Vector128.Create(Addend);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector256<T> Invoke(Vector256<T> x) => x * Vector256.Create(Multiplier) + Vector256.Create(Addend);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector512<T> Invoke(Vector512<T> x) => x * Vector512.Create(Multiplier) + Vector512.Create(Addend);
}

/// <summary><c>min(max(x, min), max)</c>.</summary>
public readonly struct ClampOperator<T>(T min, T max) : IUnaryOperator<T>
    where T : unmanaged, INumber<T>
{
    /// <summary>The lower bound.</summary>
    public T Min { get; } = min;

    /// <summary>The upper bound.</summary>
    public T Max { get; } = max;

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x) => T.Min(T.Max(x, Min), Max);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector128<T> Invoke(Vector128<T> x) => Vector128.Min(Vector128.Max(x, Vector128.Create(Min)), Vector128.Create(Max));
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector256<T> Invoke(Vector256<T> x) => Vector256.Min(Vector256.Max(x, Vector256.Create(Min)), Vector256.Create(Max));
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector512<T> Invoke(Vector512<T> x) => Vector512.Min(Vector512.Max(x, Vector512.Create(Min)), Vector512.Create(Max));
}

// ---------------------------------------------------------------- binary and reductions

/// <summary><c>x + y</c>; as a reduction, the sum (identity 0).</summary>
public readonly struct AddOperator<T> : IReduceOperator<T>
    where T : unmanaged, INumber<T>
{
    /// <inheritdoc />
    public T Identity => T.Zero;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x, T y) => x + y;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector128<T> Invoke(Vector128<T> x, Vector128<T> y) => x + y;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector256<T> Invoke(Vector256<T> x, Vector256<T> y) => x + y;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector512<T> Invoke(Vector512<T> x, Vector512<T> y) => x + y;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Reduce(Vector128<T> x) => Vector128.Sum(x);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Reduce(Vector256<T> x) => Vector256.Sum(x);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Reduce(Vector512<T> x) => Vector512.Sum(x);
}

/// <summary><c>x * y</c>; as a reduction, the product (identity 1).</summary>
public readonly struct MultiplyOperator<T> : IReduceOperator<T>
    where T : unmanaged, INumber<T>
{
    /// <inheritdoc />
    public T Identity => T.One;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x, T y) => x * y;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector128<T> Invoke(Vector128<T> x, Vector128<T> y) => x * y;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector256<T> Invoke(Vector256<T> x, Vector256<T> y) => x * y;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector512<T> Invoke(Vector512<T> x, Vector512<T> y) => x * y;
    /// <inheritdoc />
    public T Reduce(Vector128<T> x) => ReduceLanes.Product(x);
    /// <inheritdoc />
    public T Reduce(Vector256<T> x) => ReduceLanes.Product(x.GetLower() * x.GetUpper());
    /// <inheritdoc />
    public T Reduce(Vector512<T> x) => Reduce(x.GetLower() * x.GetUpper());
}

/// <summary><c>x - y</c>.</summary>
public readonly struct SubtractOperator<T> : IBinaryOperator<T>
    where T : unmanaged, INumber<T>
{
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x, T y) => x - y;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector128<T> Invoke(Vector128<T> x, Vector128<T> y) => x - y;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector256<T> Invoke(Vector256<T> x, Vector256<T> y) => x - y;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector512<T> Invoke(Vector512<T> x, Vector512<T> y) => x - y;
}

/// <summary><c>x / y</c>. Integer division has no SIMD instruction on x86, so for integers this is correct but not fast.</summary>
public readonly struct DivideOperator<T> : IBinaryOperator<T>
    where T : unmanaged, INumber<T>
{
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x, T y) => x / y;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector128<T> Invoke(Vector128<T> x, Vector128<T> y) => x / y;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector256<T> Invoke(Vector256<T> x, Vector256<T> y) => x / y;
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector512<T> Invoke(Vector512<T> x, Vector512<T> y) => x / y;
}

/// <summary><c>min(x, y)</c>; as a reduction, the minimum (identity: +infinity, or the type's maximum).</summary>
public readonly struct MinOperator<T> : IReduceOperator<T>
    where T : unmanaged, INumber<T>
{
    /// <inheritdoc />
    public T Identity => T.CreateSaturating(double.PositiveInfinity);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x, T y) => T.Min(x, y);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector128<T> Invoke(Vector128<T> x, Vector128<T> y) => Vector128.Min(x, y);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector256<T> Invoke(Vector256<T> x, Vector256<T> y) => Vector256.Min(x, y);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector512<T> Invoke(Vector512<T> x, Vector512<T> y) => Vector512.Min(x, y);
    /// <inheritdoc />
    public T Reduce(Vector128<T> x) => ReduceLanes.Min(x);
    /// <inheritdoc />
    public T Reduce(Vector256<T> x) => ReduceLanes.Min(Vector128.Min(x.GetLower(), x.GetUpper()));
    /// <inheritdoc />
    public T Reduce(Vector512<T> x) => Reduce(Vector256.Min(x.GetLower(), x.GetUpper()));
}

/// <summary><c>max(x, y)</c>; as a reduction, the maximum (identity: -infinity, or the type's minimum).</summary>
public readonly struct MaxOperator<T> : IReduceOperator<T>
    where T : unmanaged, INumber<T>
{
    /// <inheritdoc />
    public T Identity => T.CreateSaturating(double.NegativeInfinity);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x, T y) => T.Max(x, y);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector128<T> Invoke(Vector128<T> x, Vector128<T> y) => Vector128.Max(x, y);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector256<T> Invoke(Vector256<T> x, Vector256<T> y) => Vector256.Max(x, y);
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector512<T> Invoke(Vector512<T> x, Vector512<T> y) => Vector512.Max(x, y);
    /// <inheritdoc />
    public T Reduce(Vector128<T> x) => ReduceLanes.Max(x);
    /// <inheritdoc />
    public T Reduce(Vector256<T> x) => ReduceLanes.Max(Vector128.Max(x.GetLower(), x.GetUpper()));
    /// <inheritdoc />
    public T Reduce(Vector512<T> x) => Reduce(Vector256.Max(x.GetLower(), x.GetUpper()));
}

/// <summary>Horizontal reductions the vector APIs do not provide directly. Called once per chunk, not per element.</summary>
internal static class ReduceLanes
{
    public static T Min<T>(Vector128<T> x)
        where T : unmanaged, INumber<T>
    {
        T result = x.GetElement(0);
        for (int i = 1; i < Vector128<T>.Count; i++)
            result = T.Min(result, x.GetElement(i));
        return result;
    }

    public static T Max<T>(Vector128<T> x)
        where T : unmanaged, INumber<T>
    {
        T result = x.GetElement(0);
        for (int i = 1; i < Vector128<T>.Count; i++)
            result = T.Max(result, x.GetElement(i));
        return result;
    }

    public static T Product<T>(Vector128<T> x)
        where T : unmanaged, INumber<T>
    {
        T result = x.GetElement(0);
        for (int i = 1; i < Vector128<T>.Count; i++)
            result *= x.GetElement(i);
        return result;
    }
}
