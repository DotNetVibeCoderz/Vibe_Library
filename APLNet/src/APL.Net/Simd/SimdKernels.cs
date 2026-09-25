// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.CompilerServices;
using MemUnsafe = System.Runtime.CompilerServices.Unsafe;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace AplNet.Simd;

/// <summary>
/// The single-threaded vector loops every SIMD operation is built on.
/// </summary>
/// <remarks>
/// <para>
/// Each kernel takes the widest path the hardware accelerates for the element type -
/// <see cref="Vector512"/> (AVX-512), then <see cref="Vector256"/> (AVX2), then
/// <see cref="Vector128"/> (SSE / Arm AdvSimd) - and finishes the remainder with the operator's
/// scalar form. With no acceleration at all, or for an element type the vector types do not support,
/// the scalar loop does all the work, so results never depend on the hardware (except for
/// floating-point reductions, whose summation order does).
/// </para>
/// <para>
/// The main loops are unrolled four vectors deep. For transforms that keeps several loads in flight;
/// for reductions it matters more: four independent accumulators break the dependency chain through
/// the add, whose latency would otherwise cap a sum at one vector every four cycles.
/// </para>
/// <para>
/// Inputs are read through <see cref="MemoryMarshal.GetReference{T}(ReadOnlySpan{T})"/> and
/// <c>LoadUnsafe</c> with offsets the loop bounds guarantee to be in range; the public methods check
/// lengths before calling in.
/// </para>
/// </remarks>
internal static class SimdKernels
{
    public static void Transform<T, TOp>(ReadOnlySpan<T> source, Span<T> destination, TOp op)
        where T : struct
        where TOp : struct, IUnaryOperator<T>
    {
        ref T src = ref MemoryMarshal.GetReference(source);
        ref T dst = ref MemoryMarshal.GetReference(destination);
        nuint n = (nuint)source.Length;
        nuint i = 0;

        if (Vector512.IsHardwareAccelerated && Vector512<T>.IsSupported && n >= (nuint)Vector512<T>.Count)
        {
            nuint w = (nuint)Vector512<T>.Count;
            for (; i + 4 * w <= n; i += 4 * w)
            {
                Vector512<T> a = Vector512.LoadUnsafe(ref src, i);
                Vector512<T> b = Vector512.LoadUnsafe(ref src, i + w);
                Vector512<T> c = Vector512.LoadUnsafe(ref src, i + 2 * w);
                Vector512<T> d = Vector512.LoadUnsafe(ref src, i + 3 * w);
                op.Invoke(a).StoreUnsafe(ref dst, i);
                op.Invoke(b).StoreUnsafe(ref dst, i + w);
                op.Invoke(c).StoreUnsafe(ref dst, i + 2 * w);
                op.Invoke(d).StoreUnsafe(ref dst, i + 3 * w);
            }

            for (; i + w <= n; i += w)
                op.Invoke(Vector512.LoadUnsafe(ref src, i)).StoreUnsafe(ref dst, i);
        }
        else if (Vector256.IsHardwareAccelerated && Vector256<T>.IsSupported && n >= (nuint)Vector256<T>.Count)
        {
            nuint w = (nuint)Vector256<T>.Count;
            for (; i + 4 * w <= n; i += 4 * w)
            {
                Vector256<T> a = Vector256.LoadUnsafe(ref src, i);
                Vector256<T> b = Vector256.LoadUnsafe(ref src, i + w);
                Vector256<T> c = Vector256.LoadUnsafe(ref src, i + 2 * w);
                Vector256<T> d = Vector256.LoadUnsafe(ref src, i + 3 * w);
                op.Invoke(a).StoreUnsafe(ref dst, i);
                op.Invoke(b).StoreUnsafe(ref dst, i + w);
                op.Invoke(c).StoreUnsafe(ref dst, i + 2 * w);
                op.Invoke(d).StoreUnsafe(ref dst, i + 3 * w);
            }

            for (; i + w <= n; i += w)
                op.Invoke(Vector256.LoadUnsafe(ref src, i)).StoreUnsafe(ref dst, i);
        }
        else if (Vector128.IsHardwareAccelerated && Vector128<T>.IsSupported && n >= (nuint)Vector128<T>.Count)
        {
            nuint w = (nuint)Vector128<T>.Count;
            for (; i + 4 * w <= n; i += 4 * w)
            {
                Vector128<T> a = Vector128.LoadUnsafe(ref src, i);
                Vector128<T> b = Vector128.LoadUnsafe(ref src, i + w);
                Vector128<T> c = Vector128.LoadUnsafe(ref src, i + 2 * w);
                Vector128<T> d = Vector128.LoadUnsafe(ref src, i + 3 * w);
                op.Invoke(a).StoreUnsafe(ref dst, i);
                op.Invoke(b).StoreUnsafe(ref dst, i + w);
                op.Invoke(c).StoreUnsafe(ref dst, i + 2 * w);
                op.Invoke(d).StoreUnsafe(ref dst, i + 3 * w);
            }

            for (; i + w <= n; i += w)
                op.Invoke(Vector128.LoadUnsafe(ref src, i)).StoreUnsafe(ref dst, i);
        }

        for (; i < n; i++)
            MemUnsafe.Add(ref dst, i) = op.Invoke(MemUnsafe.Add(ref src, i));
    }

    public static void Transform<T, TOp>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, Span<T> destination, TOp op)
        where T : struct
        where TOp : struct, IBinaryOperator<T>
    {
        ref T xr = ref MemoryMarshal.GetReference(x);
        ref T yr = ref MemoryMarshal.GetReference(y);
        ref T dst = ref MemoryMarshal.GetReference(destination);
        nuint n = (nuint)x.Length;
        nuint i = 0;

        if (Vector512.IsHardwareAccelerated && Vector512<T>.IsSupported && n >= (nuint)Vector512<T>.Count)
        {
            nuint w = (nuint)Vector512<T>.Count;
            for (; i + 2 * w <= n; i += 2 * w)
            {
                Vector512<T> a0 = Vector512.LoadUnsafe(ref xr, i);
                Vector512<T> a1 = Vector512.LoadUnsafe(ref xr, i + w);
                Vector512<T> b0 = Vector512.LoadUnsafe(ref yr, i);
                Vector512<T> b1 = Vector512.LoadUnsafe(ref yr, i + w);
                op.Invoke(a0, b0).StoreUnsafe(ref dst, i);
                op.Invoke(a1, b1).StoreUnsafe(ref dst, i + w);
            }

            for (; i + w <= n; i += w)
                op.Invoke(Vector512.LoadUnsafe(ref xr, i), Vector512.LoadUnsafe(ref yr, i)).StoreUnsafe(ref dst, i);
        }
        else if (Vector256.IsHardwareAccelerated && Vector256<T>.IsSupported && n >= (nuint)Vector256<T>.Count)
        {
            nuint w = (nuint)Vector256<T>.Count;
            for (; i + 2 * w <= n; i += 2 * w)
            {
                Vector256<T> a0 = Vector256.LoadUnsafe(ref xr, i);
                Vector256<T> a1 = Vector256.LoadUnsafe(ref xr, i + w);
                Vector256<T> b0 = Vector256.LoadUnsafe(ref yr, i);
                Vector256<T> b1 = Vector256.LoadUnsafe(ref yr, i + w);
                op.Invoke(a0, b0).StoreUnsafe(ref dst, i);
                op.Invoke(a1, b1).StoreUnsafe(ref dst, i + w);
            }

            for (; i + w <= n; i += w)
                op.Invoke(Vector256.LoadUnsafe(ref xr, i), Vector256.LoadUnsafe(ref yr, i)).StoreUnsafe(ref dst, i);
        }
        else if (Vector128.IsHardwareAccelerated && Vector128<T>.IsSupported && n >= (nuint)Vector128<T>.Count)
        {
            nuint w = (nuint)Vector128<T>.Count;
            for (; i + 2 * w <= n; i += 2 * w)
            {
                Vector128<T> a0 = Vector128.LoadUnsafe(ref xr, i);
                Vector128<T> a1 = Vector128.LoadUnsafe(ref xr, i + w);
                Vector128<T> b0 = Vector128.LoadUnsafe(ref yr, i);
                Vector128<T> b1 = Vector128.LoadUnsafe(ref yr, i + w);
                op.Invoke(a0, b0).StoreUnsafe(ref dst, i);
                op.Invoke(a1, b1).StoreUnsafe(ref dst, i + w);
            }

            for (; i + w <= n; i += w)
                op.Invoke(Vector128.LoadUnsafe(ref xr, i), Vector128.LoadUnsafe(ref yr, i)).StoreUnsafe(ref dst, i);
        }

        for (; i < n; i++)
            MemUnsafe.Add(ref dst, i) = op.Invoke(MemUnsafe.Add(ref xr, i), MemUnsafe.Add(ref yr, i));
    }

    public static T Reduce<T, TMap, TReduce>(ReadOnlySpan<T> source, TMap map, TReduce reduce)
        where T : struct
        where TMap : struct, IUnaryOperator<T>
        where TReduce : struct, IReduceOperator<T>
    {
        ref T src = ref MemoryMarshal.GetReference(source);
        nuint n = (nuint)source.Length;
        nuint i = 0;
        T result = reduce.Identity;

        if (Vector512.IsHardwareAccelerated && Vector512<T>.IsSupported && n >= (nuint)Vector512<T>.Count)
        {
            nuint w = (nuint)Vector512<T>.Count;
            Vector512<T> identity = Vector512.Create(result);
            Vector512<T> a0 = identity, a1 = identity, a2 = identity, a3 = identity;
            for (; i + 4 * w <= n; i += 4 * w)
            {
                a0 = reduce.Invoke(a0, map.Invoke(Vector512.LoadUnsafe(ref src, i)));
                a1 = reduce.Invoke(a1, map.Invoke(Vector512.LoadUnsafe(ref src, i + w)));
                a2 = reduce.Invoke(a2, map.Invoke(Vector512.LoadUnsafe(ref src, i + 2 * w)));
                a3 = reduce.Invoke(a3, map.Invoke(Vector512.LoadUnsafe(ref src, i + 3 * w)));
            }

            for (; i + w <= n; i += w)
                a0 = reduce.Invoke(a0, map.Invoke(Vector512.LoadUnsafe(ref src, i)));

            result = reduce.Reduce(reduce.Invoke(reduce.Invoke(a0, a1), reduce.Invoke(a2, a3)));
        }
        else if (Vector256.IsHardwareAccelerated && Vector256<T>.IsSupported && n >= (nuint)Vector256<T>.Count)
        {
            nuint w = (nuint)Vector256<T>.Count;
            Vector256<T> identity = Vector256.Create(result);
            Vector256<T> a0 = identity, a1 = identity, a2 = identity, a3 = identity;
            for (; i + 4 * w <= n; i += 4 * w)
            {
                a0 = reduce.Invoke(a0, map.Invoke(Vector256.LoadUnsafe(ref src, i)));
                a1 = reduce.Invoke(a1, map.Invoke(Vector256.LoadUnsafe(ref src, i + w)));
                a2 = reduce.Invoke(a2, map.Invoke(Vector256.LoadUnsafe(ref src, i + 2 * w)));
                a3 = reduce.Invoke(a3, map.Invoke(Vector256.LoadUnsafe(ref src, i + 3 * w)));
            }

            for (; i + w <= n; i += w)
                a0 = reduce.Invoke(a0, map.Invoke(Vector256.LoadUnsafe(ref src, i)));

            result = reduce.Reduce(reduce.Invoke(reduce.Invoke(a0, a1), reduce.Invoke(a2, a3)));
        }
        else if (Vector128.IsHardwareAccelerated && Vector128<T>.IsSupported && n >= (nuint)Vector128<T>.Count)
        {
            nuint w = (nuint)Vector128<T>.Count;
            Vector128<T> identity = Vector128.Create(result);
            Vector128<T> a0 = identity, a1 = identity, a2 = identity, a3 = identity;
            for (; i + 4 * w <= n; i += 4 * w)
            {
                a0 = reduce.Invoke(a0, map.Invoke(Vector128.LoadUnsafe(ref src, i)));
                a1 = reduce.Invoke(a1, map.Invoke(Vector128.LoadUnsafe(ref src, i + w)));
                a2 = reduce.Invoke(a2, map.Invoke(Vector128.LoadUnsafe(ref src, i + 2 * w)));
                a3 = reduce.Invoke(a3, map.Invoke(Vector128.LoadUnsafe(ref src, i + 3 * w)));
            }

            for (; i + w <= n; i += w)
                a0 = reduce.Invoke(a0, map.Invoke(Vector128.LoadUnsafe(ref src, i)));

            result = reduce.Reduce(reduce.Invoke(reduce.Invoke(a0, a1), reduce.Invoke(a2, a3)));
        }

        for (; i < n; i++)
            result = reduce.Invoke(result, map.Invoke(MemUnsafe.Add(ref src, i)));

        return result;
    }

    public static T Reduce<T, TMap, TReduce>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, TMap map, TReduce reduce)
        where T : struct
        where TMap : struct, IBinaryOperator<T>
        where TReduce : struct, IReduceOperator<T>
    {
        ref T xr = ref MemoryMarshal.GetReference(x);
        ref T yr = ref MemoryMarshal.GetReference(y);
        nuint n = (nuint)x.Length;
        nuint i = 0;
        T result = reduce.Identity;

        if (Vector512.IsHardwareAccelerated && Vector512<T>.IsSupported && n >= (nuint)Vector512<T>.Count)
        {
            nuint w = (nuint)Vector512<T>.Count;
            Vector512<T> identity = Vector512.Create(result);
            Vector512<T> a0 = identity, a1 = identity, a2 = identity, a3 = identity;
            for (; i + 4 * w <= n; i += 4 * w)
            {
                a0 = reduce.Invoke(a0, map.Invoke(Vector512.LoadUnsafe(ref xr, i), Vector512.LoadUnsafe(ref yr, i)));
                a1 = reduce.Invoke(a1, map.Invoke(Vector512.LoadUnsafe(ref xr, i + w), Vector512.LoadUnsafe(ref yr, i + w)));
                a2 = reduce.Invoke(a2, map.Invoke(Vector512.LoadUnsafe(ref xr, i + 2 * w), Vector512.LoadUnsafe(ref yr, i + 2 * w)));
                a3 = reduce.Invoke(a3, map.Invoke(Vector512.LoadUnsafe(ref xr, i + 3 * w), Vector512.LoadUnsafe(ref yr, i + 3 * w)));
            }

            for (; i + w <= n; i += w)
                a0 = reduce.Invoke(a0, map.Invoke(Vector512.LoadUnsafe(ref xr, i), Vector512.LoadUnsafe(ref yr, i)));

            result = reduce.Reduce(reduce.Invoke(reduce.Invoke(a0, a1), reduce.Invoke(a2, a3)));
        }
        else if (Vector256.IsHardwareAccelerated && Vector256<T>.IsSupported && n >= (nuint)Vector256<T>.Count)
        {
            nuint w = (nuint)Vector256<T>.Count;
            Vector256<T> identity = Vector256.Create(result);
            Vector256<T> a0 = identity, a1 = identity, a2 = identity, a3 = identity;
            for (; i + 4 * w <= n; i += 4 * w)
            {
                a0 = reduce.Invoke(a0, map.Invoke(Vector256.LoadUnsafe(ref xr, i), Vector256.LoadUnsafe(ref yr, i)));
                a1 = reduce.Invoke(a1, map.Invoke(Vector256.LoadUnsafe(ref xr, i + w), Vector256.LoadUnsafe(ref yr, i + w)));
                a2 = reduce.Invoke(a2, map.Invoke(Vector256.LoadUnsafe(ref xr, i + 2 * w), Vector256.LoadUnsafe(ref yr, i + 2 * w)));
                a3 = reduce.Invoke(a3, map.Invoke(Vector256.LoadUnsafe(ref xr, i + 3 * w), Vector256.LoadUnsafe(ref yr, i + 3 * w)));
            }

            for (; i + w <= n; i += w)
                a0 = reduce.Invoke(a0, map.Invoke(Vector256.LoadUnsafe(ref xr, i), Vector256.LoadUnsafe(ref yr, i)));

            result = reduce.Reduce(reduce.Invoke(reduce.Invoke(a0, a1), reduce.Invoke(a2, a3)));
        }
        else if (Vector128.IsHardwareAccelerated && Vector128<T>.IsSupported && n >= (nuint)Vector128<T>.Count)
        {
            nuint w = (nuint)Vector128<T>.Count;
            Vector128<T> identity = Vector128.Create(result);
            Vector128<T> a0 = identity, a1 = identity, a2 = identity, a3 = identity;
            for (; i + 4 * w <= n; i += 4 * w)
            {
                a0 = reduce.Invoke(a0, map.Invoke(Vector128.LoadUnsafe(ref xr, i), Vector128.LoadUnsafe(ref yr, i)));
                a1 = reduce.Invoke(a1, map.Invoke(Vector128.LoadUnsafe(ref xr, i + w), Vector128.LoadUnsafe(ref yr, i + w)));
                a2 = reduce.Invoke(a2, map.Invoke(Vector128.LoadUnsafe(ref xr, i + 2 * w), Vector128.LoadUnsafe(ref yr, i + 2 * w)));
                a3 = reduce.Invoke(a3, map.Invoke(Vector128.LoadUnsafe(ref xr, i + 3 * w), Vector128.LoadUnsafe(ref yr, i + 3 * w)));
            }

            for (; i + w <= n; i += w)
                a0 = reduce.Invoke(a0, map.Invoke(Vector128.LoadUnsafe(ref xr, i), Vector128.LoadUnsafe(ref yr, i)));

            result = reduce.Reduce(reduce.Invoke(reduce.Invoke(a0, a1), reduce.Invoke(a2, a3)));
        }

        for (; i < n; i++)
            result = reduce.Invoke(result, map.Invoke(MemUnsafe.Add(ref xr, i), MemUnsafe.Add(ref yr, i)));

        return result;
    }

    public static void TransformWithDelegates<T>(Span<T> data, Func<Vector256<T>, Vector256<T>> vectorOp, Func<T, T> scalarOp)
        where T : struct
    {
        ref T r = ref MemoryMarshal.GetReference(data);
        nuint n = (nuint)data.Length;
        nuint i = 0;

        // Vector256 only when it is real hardware: on a 128-bit-only CPU it is emulated, and a delegate
        // call per emulated vector would be slower than the scalar fallback.
        if (Vector256.IsHardwareAccelerated && Vector256<T>.IsSupported)
        {
            nuint w = (nuint)Vector256<T>.Count;
            for (; i + w <= n; i += w)
                vectorOp(Vector256.LoadUnsafe(ref r, i)).StoreUnsafe(ref r, i);
        }

        for (; i < n; i++)
            MemUnsafe.Add(ref r, i) = scalarOp(MemUnsafe.Add(ref r, i));
    }
}
