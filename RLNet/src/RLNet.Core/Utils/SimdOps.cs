// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace RLNet.Utils;

/// <summary>
/// Vectorised primitives over <see cref="float"/> spans.
/// </summary>
/// <remarks>
/// <para>
/// These four operations — dot, axpy, scaled add and elementwise multiply-add — are what a dense
/// layer decomposes into, and a training run spends most of its time inside them. They use
/// <see cref="Vector{T}"/>, which the JIT widens to whatever the host offers (AVX-512, AVX2,
/// NEON), so one implementation covers x64 and ARM without an intrinsics matrix.
/// </para>
/// <para>
/// Every method takes spans and writes in place. Nothing here allocates, which is the point:
/// the alternative is a tensor type whose every operator returns a new array, and at a few
/// million gradient steps that is a garbage collector running flat out.
/// </para>
/// </remarks>
public static class SimdOps
{
    private static readonly int Width = Vector<float>.Count;

    /// <summary>Returns the dot product of two equal-length spans.</summary>
    public static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        int n = a.Length;
        int i = 0;
        var acc = Vector<float>.Zero;

        for (; i <= n - Width; i += Width)
            acc += new Vector<float>(a.Slice(i, Width)) * new Vector<float>(b.Slice(i, Width));

        float sum = Vector.Sum(acc);
        for (; i < n; i++) sum += a[i] * b[i];
        return sum;
    }

    /// <summary>Computes <c>y += alpha * x</c> in place.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddScaled(Span<float> y, ReadOnlySpan<float> x, float alpha)
    {
        int n = y.Length;
        int i = 0;
        var va = new Vector<float>(alpha);

        for (; i <= n - Width; i += Width)
        {
            var acc = new Vector<float>(y.Slice(i, Width)) + va * new Vector<float>(x.Slice(i, Width));
            acc.CopyTo(y.Slice(i, Width));
        }
        for (; i < n; i++) y[i] += alpha * x[i];
    }

    /// <summary>Computes <c>y = y * decay + x * (1 - decay)</c> in place, the Polyak average.</summary>
    /// <remarks>
    /// Used for soft target-network updates in DQN, SAC and TD3. Fusing it into one pass keeps
    /// the target parameters in cache instead of streaming them twice.
    /// </remarks>
    public static void PolyakBlend(Span<float> y, ReadOnlySpan<float> x, float tau)
    {
        int n = y.Length;
        int i = 0;
        var vKeep = new Vector<float>(1f - tau);
        var vNew = new Vector<float>(tau);

        for (; i <= n - Width; i += Width)
        {
            var acc = vKeep * new Vector<float>(y.Slice(i, Width)) + vNew * new Vector<float>(x.Slice(i, Width));
            acc.CopyTo(y.Slice(i, Width));
        }
        for (; i < n; i++) y[i] = y[i] * (1f - tau) + x[i] * tau;
    }

    /// <summary>Scales a span in place.</summary>
    public static void Scale(Span<float> y, float alpha)
    {
        int n = y.Length;
        int i = 0;
        var va = new Vector<float>(alpha);

        for (; i <= n - Width; i += Width)
            (va * new Vector<float>(y.Slice(i, Width))).CopyTo(y.Slice(i, Width));
        for (; i < n; i++) y[i] *= alpha;
    }

    /// <summary>Returns the sum of squares, used for gradient-norm clipping.</summary>
    public static float SumSquares(ReadOnlySpan<float> x)
    {
        int n = x.Length;
        int i = 0;
        var acc = Vector<float>.Zero;

        for (; i <= n - Width; i += Width)
        {
            var v = new Vector<float>(x.Slice(i, Width));
            acc += v * v;
        }

        float sum = Vector.Sum(acc);
        for (; i < n; i++) sum += x[i] * x[i];
        return sum;
    }

    /// <summary>Returns the largest value in a span, and its index through <paramref name="argMax"/>.</summary>
    /// <remarks>
    /// Greedy action selection runs this on every step of every episode. The scalar loop is kept
    /// deliberately branch-light; vectorising an argmax needs an index vector and a blend, which
    /// does not pay off at the action-count sizes RL uses (2 to a few dozen).
    /// </remarks>
    public static float Max(ReadOnlySpan<float> x, out int argMax)
    {
        float best = x[0];
        int bestIndex = 0;
        for (int i = 1; i < x.Length; i++)
        {
            if (x[i] > best)
            {
                best = x[i];
                bestIndex = i;
            }
        }
        argMax = bestIndex;
        return best;
    }

    /// <summary>Returns the largest value in a span.</summary>
    public static float Max(ReadOnlySpan<float> x) => Max(x, out _);

    /// <summary>
    /// Converts logits into probabilities in place, shifting by the maximum first so that
    /// large logits cannot overflow <c>exp</c>.
    /// </summary>
    public static void SoftmaxInPlace(Span<float> logits)
    {
        float max = Max(logits);
        float sum = 0f;
        for (int i = 0; i < logits.Length; i++)
        {
            float e = MathF.Exp(logits[i] - max);
            logits[i] = e;
            sum += e;
        }
        Scale(logits, 1f / sum);
    }
}
