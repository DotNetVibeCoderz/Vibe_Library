// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet.Simd;

namespace AplNet.Tests;

/// <summary>
/// Exp and Log are checked against the scalar functions within a relative tolerance (the vector
/// forms are accurate to a few ulp, not bit-identical). The fused multiply-add must match
/// <see cref="Math.FusedMultiplyAdd(double, double, double)"/> exactly.
/// </summary>
public class SimdMathOperatorTests
{
    private static readonly int[] s_lengths = [0, 1, 3, 4, 7, 8, 15, 16, 17, 31, 33, 63, 64, 65, 127, 1000, 4099];

    [Fact]
    public void ExpMatchesScalar()
    {
        foreach (int n in s_lengths)
        {
            double[] x = Enumerable.Range(0, n).Select(i => -30 + 60.0 * i / Math.Max(1, n - 1)).ToArray();
            AssertClose(x.Select(Math.Exp).ToArray(), Map(x, new ExpOperator<double>()), 1e-14);

            float[] xf = x.Select(v => (float)v).ToArray();
            AssertClose(xf.Select(v => (double)MathF.Exp(v)).ToArray(), Map(xf, new ExpOperator<float>()).Select(v => (double)v).ToArray(), 1e-6);
        }
    }

    [Fact]
    public void LogMatchesScalar()
    {
        foreach (int n in s_lengths)
        {
            double[] x = Enumerable.Range(0, n).Select(i => Math.Pow(10, -8 + 16.0 * i / Math.Max(1, n - 1))).ToArray();
            AssertClose(x.Select(v => Math.Log(v)).ToArray(), Map(x, new LogOperator<double>()), 1e-14);

            float[] xf = x.Select(v => (float)v).ToArray();
            AssertClose(xf.Select(v => (double)MathF.Log(v)).ToArray(), Map(xf, new LogOperator<float>()).Select(v => (double)v).ToArray(), 1e-6);
        }
    }

    [Fact]
    public void ExpAndLogHandleSpecialValues()
    {
        double[] x = [0, 1, double.PositiveInfinity, double.NegativeInfinity, double.NaN, 1000, -1000, -1, 0, 1, 2, 3, 4, 5, 6, 7];
        double[] exp = Map(x, new ExpOperator<double>());
        Assert.Equal(1, exp[0]);
        Assert.Equal(double.PositiveInfinity, exp[2]);
        Assert.Equal(0, exp[3]);
        Assert.True(double.IsNaN(exp[4]));
        Assert.Equal(double.PositiveInfinity, exp[5]);
        Assert.Equal(0, exp[6]);

        double[] log = Map(x, new LogOperator<double>());
        Assert.Equal(double.NegativeInfinity, log[0]);
        Assert.Equal(0, log[1]);
        Assert.Equal(double.PositiveInfinity, log[2]);
        Assert.True(double.IsNaN(log[3]));
        Assert.True(double.IsNaN(log[7]));
    }

    [Fact]
    public void FusedMultiplyAddIsExact()
    {
        var random = new Random(42);
        foreach (int n in s_lengths)
        {
            double[] x = Enumerable.Range(0, n).Select(_ => random.NextDouble() * 1e6 - 5e5).ToArray();
            const double a = 1.000000000000001, b = -0.3333333333333333;
            Assert.Equal(x.Select(v => Math.FusedMultiplyAdd(v, a, b)).ToArray(), Map(x, new FusedMultiplyAddOperator<double>(a, b)));

            float[] xf = x.Select(v => (float)v).ToArray();
            const float af = 1.0000001f, bf = -0.33333334f;
            Assert.Equal(xf.Select(v => MathF.FusedMultiplyAdd(v, af, bf)).ToArray(), Map(xf, new FusedMultiplyAddOperator<float>(af, bf)));
        }
    }

    [Fact]
    public void FusedMultiplyAddRoundsOnce()
    {
        // 0.1 * 10 - 1 is exactly 0 with two roundings, and the rounding error of 0.1 with one.
        double[] x = Enumerable.Repeat(0.1, 64).ToArray();
        Assert.All(Map(x, new MultiplyAddOperator<double>(10, -1)), v => Assert.Equal(0.0, v));
        Assert.All(Map(x, new FusedMultiplyAddOperator<double>(10, -1)), v => Assert.Equal(Math.FusedMultiplyAdd(0.1, 10, -1), v));
        Assert.NotEqual(0.0, Math.FusedMultiplyAdd(0.1, 10, -1));
    }

    [Fact]
    public void ParallelTransformsUseTheNewOperators()
    {
        double[] x = Enumerable.Range(1, 200_000).Select(i => i * 1e-3).ToArray();
        double[] y = new double[x.Length];
        var options = new AplOptions { MinChunkSize = 10_000 };
        SimdOps.ParallelTransform(x, y, new LogOperator<double>(), options);
        SimdOps.ParallelTransformInPlace(y, new ExpOperator<double>(), options);
        AssertClose(x, y, 1e-13);
    }

    private static T[] Map<T, TOp>(T[] source, TOp op)
        where T : struct
        where TOp : struct, IUnaryOperator<T>
    {
        var destination = new T[source.Length];
        SimdOps.Transform<T, TOp>(source, destination, op);
        return destination;
    }

    private static void AssertClose(double[] expected, double[] actual, double relative)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            double e = expected[i], a = actual[i];
            if (double.IsNaN(e) || double.IsInfinity(e) || e == 0)
            {
                Assert.True(e.Equals(a) || Math.Abs(a) < 1e-300, $"[{i}] expected {e}, got {a}");
                continue;
            }

            Assert.True(Math.Abs(a - e) <= relative * Math.Abs(e), $"[{i}] expected {e:R}, got {a:R} (relative error {Math.Abs(a - e) / Math.Abs(e):E2})");
        }
    }
}
