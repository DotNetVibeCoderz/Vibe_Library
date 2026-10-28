// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Numerics;
using System.Runtime.CompilerServices;
using MemUnsafe = System.Runtime.CompilerServices.Unsafe;
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

/// <summary>
/// <c>e^x</c>, for <c>float</c> and <c>double</c>. The vector form is the runtime's own vectorized
/// <c>Exp</c>: accurate to within a few ulp of <see cref="Math.Exp(double)"/>, not bit-identical
/// to it, so a vectorized result can differ from the scalar tail in the last digits.
/// </summary>
public readonly struct ExpOperator<T> : IUnaryOperator<T>
    where T : unmanaged, IExponentialFunctions<T>
{
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x) => T.Exp(x);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector128<T> Invoke(Vector128<T> x)
    {
        if (typeof(T) == typeof(float))
            return Vector128.Exp(x.As<T, float>()).As<float, T>();
        if (typeof(T) == typeof(double))
            return Vector128.Exp(x.As<T, double>()).As<double, T>();
        throw FloatingOnly();
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector256<T> Invoke(Vector256<T> x)
    {
        if (typeof(T) == typeof(float))
            return Vector256.Exp(x.As<T, float>()).As<float, T>();
        if (typeof(T) == typeof(double))
            return Vector256.Exp(x.As<T, double>()).As<double, T>();
        throw FloatingOnly();
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector512<T> Invoke(Vector512<T> x)
    {
        if (typeof(T) == typeof(float))
            return Vector512.Exp(x.As<T, float>()).As<float, T>();
        if (typeof(T) == typeof(double))
            return Vector512.Exp(x.As<T, double>()).As<double, T>();
        throw FloatingOnly();
    }

    private static NotSupportedException FloatingOnly() => new($"{nameof(ExpOperator<T>)} is vectorized for float and double only, not {typeof(T).Name}.");
}

/// <summary>
/// The natural logarithm <c>ln(x)</c>, for <c>float</c> and <c>double</c>. Like
/// <see cref="ExpOperator{T}"/>, the vector form is within a few ulp of the scalar one rather than
/// bit-identical to it.
/// </summary>
public readonly struct LogOperator<T> : IUnaryOperator<T>
    where T : unmanaged, ILogarithmicFunctions<T>
{
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x) => T.Log(x);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector128<T> Invoke(Vector128<T> x)
    {
        if (typeof(T) == typeof(float))
            return Vector128.Log(x.As<T, float>()).As<float, T>();
        if (typeof(T) == typeof(double))
            return Vector128.Log(x.As<T, double>()).As<double, T>();
        throw FloatingOnly();
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector256<T> Invoke(Vector256<T> x)
    {
        if (typeof(T) == typeof(float))
            return Vector256.Log(x.As<T, float>()).As<float, T>();
        if (typeof(T) == typeof(double))
            return Vector256.Log(x.As<T, double>()).As<double, T>();
        throw FloatingOnly();
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector512<T> Invoke(Vector512<T> x)
    {
        if (typeof(T) == typeof(float))
            return Vector512.Log(x.As<T, float>()).As<float, T>();
        if (typeof(T) == typeof(double))
            return Vector512.Log(x.As<T, double>()).As<double, T>();
        throw FloatingOnly();
    }

    private static NotSupportedException FloatingOnly() => new($"{nameof(LogOperator<T>)} is vectorized for float and double only, not {typeof(T).Name}.");
}

/// <summary>
/// <c>x * multiplier + addend</c> as a fused multiply-add: one rounding instead of the two of
/// <see cref="MultiplyAddOperator{T}"/>. The explicit opt-in, for <c>float</c> and <c>double</c>.
/// Results match <see cref="Math.FusedMultiplyAdd(double, double, double)"/> bit for bit in every
/// lane, so they can differ from <see cref="MultiplyAddOperator{T}"/> in the last bit. On hardware
/// without FMA instructions the runtime emulates it exactly, which is slow.
/// </summary>
public readonly struct FusedMultiplyAddOperator<T>(T multiplier, T addend) : IUnaryOperator<T>
    where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>The multiplier <c>a</c> in <c>fma(x, a, b)</c>.</summary>
    public T Multiplier { get; } = multiplier;

    /// <summary>The addend <c>b</c> in <c>fma(x, a, b)</c>.</summary>
    public T Addend { get; } = addend;

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public T Invoke(T x) => T.FusedMultiplyAdd(x, Multiplier, Addend);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector128<T> Invoke(Vector128<T> x)
    {
        if (typeof(T) == typeof(float))
            return Vector128.FusedMultiplyAdd(x.As<T, float>(), Vector128.Create(MemUnsafe.BitCast<T, float>(Multiplier)), Vector128.Create(MemUnsafe.BitCast<T, float>(Addend))).As<float, T>();
        if (typeof(T) == typeof(double))
            return Vector128.FusedMultiplyAdd(x.As<T, double>(), Vector128.Create(MemUnsafe.BitCast<T, double>(Multiplier)), Vector128.Create(MemUnsafe.BitCast<T, double>(Addend))).As<double, T>();
        throw FloatingOnly();
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector256<T> Invoke(Vector256<T> x)
    {
        if (typeof(T) == typeof(float))
            return Vector256.FusedMultiplyAdd(x.As<T, float>(), Vector256.Create(MemUnsafe.BitCast<T, float>(Multiplier)), Vector256.Create(MemUnsafe.BitCast<T, float>(Addend))).As<float, T>();
        if (typeof(T) == typeof(double))
            return Vector256.FusedMultiplyAdd(x.As<T, double>(), Vector256.Create(MemUnsafe.BitCast<T, double>(Multiplier)), Vector256.Create(MemUnsafe.BitCast<T, double>(Addend))).As<double, T>();
        throw FloatingOnly();
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector512<T> Invoke(Vector512<T> x)
    {
        if (typeof(T) == typeof(float))
            return Vector512.FusedMultiplyAdd(x.As<T, float>(), Vector512.Create(MemUnsafe.BitCast<T, float>(Multiplier)), Vector512.Create(MemUnsafe.BitCast<T, float>(Addend))).As<float, T>();
        if (typeof(T) == typeof(double))
            return Vector512.FusedMultiplyAdd(x.As<T, double>(), Vector512.Create(MemUnsafe.BitCast<T, double>(Multiplier)), Vector512.Create(MemUnsafe.BitCast<T, double>(Addend))).As<double, T>();
        throw FloatingOnly();
    }

    private static NotSupportedException FloatingOnly() => new($"{nameof(FusedMultiplyAddOperator<T>)} is vectorized for float and double only, not {typeof(T).Name}.");
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
