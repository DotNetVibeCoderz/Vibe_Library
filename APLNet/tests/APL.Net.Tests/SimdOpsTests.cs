// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Numerics;
using System.Runtime.Intrinsics;
using AplNet.Simd;

namespace AplNet.Tests;

/// <summary>
/// Every kernel against a scalar reference, for each FR2 element type and for lengths that hit every
/// path: empty, shorter than one vector, the unrolled loop, the single-vector loop and the scalar tail.
/// Run the suite with DOTNET_EnableAVX512F=0, DOTNET_EnableAVX2=0 or DOTNET_EnableHWIntrinsic=0 to
/// exercise the narrower and scalar fallbacks (CI does).
/// </summary>
public class SimdOpsTests
{
    public static readonly int[] Lengths = [0, 1, 3, 7, 8, 15, 16, 17, 31, 32, 33, 63, 64, 65, 127, 129, 1000, 4099, 100_003];

    private static T[] Data<T>(int length, int seed)
        where T : unmanaged, INumber<T>
    {
        var random = new Random(seed);
        var result = new T[length];
        for (int i = 0; i < length; i++)
            result[i] = T.CreateTruncating(random.Next(-1000, 1000)) / T.CreateTruncating(typeof(T) == typeof(float) || typeof(T) == typeof(double) ? 8 : 1);
        return result;
    }

    private static void AssertClose<T>(T expected, T actual, int length)
        where T : unmanaged, INumber<T>
    {
        if (typeof(T) == typeof(float) || typeof(T) == typeof(double))
        {
            // Vector sums add in a different order; allow for rounding proportional to the length.
            double e = double.CreateTruncating(expected), a = double.CreateTruncating(actual);
            double tolerance = (typeof(T) == typeof(float) ? 1e-4 : 1e-10) * Math.Max(1, Math.Abs(e)) + 1e-6 * length;
            Assert.True(Math.Abs(e - a) <= tolerance, $"expected {e}, got {a} (length {length})");
        }
        else
        {
            Assert.Equal(expected, actual);
        }
    }

    // ---------------------------------------------------------------- transforms

    [Fact] public void TransformFloat() => CheckTransforms<float>();
    [Fact] public void TransformDouble() => CheckTransforms<double>();
    [Fact] public void TransformInt() => CheckTransforms<int>();
    [Fact] public void TransformLong() => CheckTransforms<long>();

    private static void CheckTransforms<T>()
        where T : unmanaged, INumber<T>
    {
        T a = T.CreateTruncating(3), b = T.CreateTruncating(-7);
        foreach (int length in Lengths)
        {
            T[] source = Data<T>(length, length);
            T[] expected = source.Select(x => x * a + b).ToArray();

            T[] inPlace = (T[])source.Clone();
            SimdOps.TransformInPlace(inPlace.AsSpan(), new MultiplyAddOperator<T>(a, b));
            Assert.Equal(expected, inPlace);

            var destination = new T[length];
            SimdOps.Transform<T, MultiplyAddOperator<T>>(source, destination, new MultiplyAddOperator<T>(a, b));
            Assert.Equal(expected, destination);

            T[] parallel = (T[])source.Clone();
            SimdOps.ParallelTransformInPlace(parallel, new MultiplyAddOperator<T>(a, b), new AplOptions { MinChunkSize = 1000 });
            Assert.Equal(expected, parallel);

            var parallelDestination = new T[length];
            SimdOps.ParallelTransform(source, parallelDestination, new MultiplyAddOperator<T>(a, b), new AplOptions { MinChunkSize = 1000 });
            Assert.Equal(expected, parallelDestination);

            T[] y = Data<T>(length, length + 1);
            var sum = new T[length];
            SimdOps.ParallelTransform(source, y, sum, default(AddOperator<T>), new AplOptions { MinChunkSize = 1000 });
            Assert.Equal(source.Zip(y, (p, q) => p + q).ToArray(), sum);

            var difference = new T[length];
            SimdOps.Transform<T, SubtractOperator<T>>(source, y, difference, default);
            Assert.Equal(source.Zip(y, (p, q) => p - q).ToArray(), difference);

            var product = new T[length];
            SimdOps.Transform<T, MultiplyOperator<T>>(source, y, product, default);
            Assert.Equal(source.Zip(y, (p, q) => p * q).ToArray(), product);
        }
    }

    [Fact]
    public void UnaryBuiltInsMatchTheirScalarForms()
    {
        foreach (int length in Lengths)
        {
            float[] source = Data<float>(length, 99);
            Check(source, new NegateOperator<float>(), x => -x);
            Check(source, new AbsOperator<float>(), MathF.Abs);
            Check(source, new SquareOperator<float>(), x => x * x);
            Check(source.Select(MathF.Abs).ToArray(), new SqrtOperator<float>(), MathF.Sqrt);
            Check(source, new ScaleOperator<float>(0.5f), x => x * 0.5f);
            Check(source, new AddScalarOperator<float>(2), x => x + 2);
            Check(source, new ClampOperator<float>(-10, 10), x => Math.Clamp(x, -10, 10));

            int[] ints = Data<int>(length, 5);
            Check(ints, new AbsOperator<int>(), Math.Abs);
            Check(ints, new ClampOperator<int>(-100, 100), x => Math.Clamp(x, -100, 100));
        }

        static void Check<T, TOp>(T[] source, TOp op, Func<T, T> reference)
            where T : unmanaged
            where TOp : struct, IUnaryOperator<T>
        {
            T[] data = (T[])source.Clone();
            SimdOps.TransformInPlace(data.AsSpan(), op);
            Assert.Equal(source.Select(reference).ToArray(), data);
        }
    }

    [Fact]
    public void DelegateTransformMatchesScalar()
    {
        foreach (int length in Lengths)
        {
            float[] source = Data<float>(length, 3);
            float[] expected = source.Select(x => x * 2.5f + 1).ToArray();

            float[] data = (float[])source.Clone();
            SimdOps.TransformInPlace(data.AsSpan(), v => v * Vector256.Create(2.5f) + Vector256.Create(1f), x => x * 2.5f + 1);
            Assert.Equal(expected, data);

            float[] parallel = (float[])source.Clone();
            SimdOps.ParallelTransformInPlace(parallel, v => v * Vector256.Create(2.5f) + Vector256.Create(1f), x => x * 2.5f + 1, new AplOptions { MinChunkSize = 512 });
            Assert.Equal(expected, parallel);
        }
    }

    [Fact]
    public void DestinationValidation()
    {
        var data = new float[100];
        Assert.Throws<ArgumentException>(() => SimdOps.Transform<float, NegateOperator<float>>(data, new float[99], default));
        Assert.Throws<ArgumentException>(() => SimdOps.Transform<float, NegateOperator<float>>(data.AsSpan(0, 50), data.AsSpan(1, 50), default));
        Assert.Throws<ArgumentException>(() => SimdOps.Dot<float>(new float[3], new float[4]));
        Assert.Throws<ArgumentException>(() => SimdOps.ParallelTransform(new float[3], new float[4], new float[3], default(AddOperator<float>)));

        // Exactly the same memory is fine: that is an in-place transform.
        SimdOps.Transform<float, NegateOperator<float>>(data, data, default);
    }

    // ---------------------------------------------------------------- reductions

    [Fact] public void ReduceFloat() => CheckReductions<float>();
    [Fact] public void ReduceDouble() => CheckReductions<double>();
    [Fact] public void ReduceInt() => CheckReductions<int>();
    [Fact] public void ReduceLong() => CheckReductions<long>();

    private static void CheckReductions<T>()
        where T : unmanaged, INumber<T>
    {
        foreach (int length in Lengths)
        {
            T[] x = Data<T>(length, length * 7);
            T[] y = Data<T>(length, length * 7 + 1);
            var small = new AplOptions { MinChunkSize = 1000 };

            T sum = T.Zero, squares = T.Zero, dot = T.Zero, product = T.One;
            foreach (T v in x)
            {
                sum += v;
                squares += v * v;
            }

            for (int i = 0; i < length; i++)
                dot += x[i] * y[i];

            AssertClose(sum, SimdOps.Sum<T>(x), length);
            AssertClose(sum, SimdOps.ParallelSum(x, small), length);
            AssertClose(sum, SimdOps.Reduce<T, AddOperator<T>>(x, default), length);
            AssertClose(squares, SimdOps.SumOfSquares<T>(x), length);
            AssertClose(squares, SimdOps.ParallelMapReduce(x, default(SquareOperator<T>), default(AddOperator<T>), small), length);
            AssertClose(dot, SimdOps.Dot<T>(x, y), length);
            AssertClose(dot, SimdOps.ParallelDot(x, y, small), length);

            if (length == 0)
            {
                Assert.Throws<InvalidOperationException>(() => SimdOps.Min<T>(x));
                Assert.Throws<InvalidOperationException>(() => SimdOps.ParallelMax(x));
                Assert.Equal(T.One, SimdOps.Reduce<T, MultiplyOperator<T>>(x, default));
                continue;
            }

            Assert.Equal(x.Min(), SimdOps.Min<T>(x));
            Assert.Equal(x.Max(), SimdOps.Max<T>(x));
            Assert.Equal(x.Min(), SimdOps.ParallelMin(x, small));
            Assert.Equal(x.Max(), SimdOps.ParallelMax(x, small));

            if (length <= 17)
            {
                foreach (T v in x)
                    product *= v;
                AssertClose(product, SimdOps.Reduce<T, MultiplyOperator<T>>(x, default), length);
            }
        }
    }

    [Fact]
    public void MinAndMaxHandleInfinitiesAndExtremes()
    {
        float[] infinities = Enumerable.Repeat(float.PositiveInfinity, 50).ToArray();
        Assert.Equal(float.PositiveInfinity, SimdOps.Min<float>(infinities));
        Assert.Equal(float.NegativeInfinity, SimdOps.Max<float>(infinities.Select(v => -v).ToArray()));

        int[] extremes = Enumerable.Repeat(int.MaxValue, 40).Append(int.MinValue).ToArray();
        Assert.Equal(int.MinValue, SimdOps.Min<int>(extremes));
        Assert.Equal(int.MaxValue, SimdOps.Max<int>(extremes));
    }

    [Fact]
    public void IntegerSumsWrapExactlyAsSequentialCodeDoes()
    {
        int[] data = Enumerable.Repeat(int.MaxValue, 1_000_001).ToArray();
        int expected = 0;
        foreach (int v in data)
            expected = unchecked(expected + v);
        Assert.Equal(expected, SimdOps.ParallelSum(data, new AplOptions { MinChunkSize = 1000 }));
    }

    [Fact]
    public void ParallelOperationsAcceptMemorySlices()
    {
        double[] backing = Enumerable.Range(0, 100_000).Select(i => (double)i).ToArray();
        ReadOnlyMemory<double> slice = backing.AsMemory(10, 50_000);
        Assert.Equal(Enumerable.Range(10, 50_000).Sum(i => (double)i), SimdOps.ParallelSum(slice, new AplOptions { MinChunkSize = 1000 }));

        Memory<double> writable = backing.AsMemory(0, 10);
        SimdOps.ParallelTransformInPlace(writable, new ScaleOperator<double>(-1));
        Assert.Equal(-9.0, backing[9]);
        Assert.Equal(10.0, backing[10]);
    }

    [Fact]
    public void CapabilitiesAreConsistent()
    {
        int width = SimdCapabilities.PreferredVectorWidth;
        Assert.Contains(width, new[] { 0, 128, 256, 512 });
        Assert.Equal(width == 512, SimdCapabilities.IsVector512Accelerated);
        Assert.False(string.IsNullOrWhiteSpace(SimdCapabilities.Describe()));
    }
}
