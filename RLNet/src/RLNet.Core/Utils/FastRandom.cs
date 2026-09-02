// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
// Created by Gravicode Studios, led by Kang Fadhil.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace RLNet.Utils;

/// <summary>
/// xoshiro256++ pseudo-random generator with a Gaussian sampler.
/// </summary>
/// <remarks>
/// <para>
/// RL burns random numbers at the same rate it burns environment steps: an epsilon-greedy
/// draw and an action draw on every single step, plus a batch of replay indices on every
/// gradient step. <see cref="System.Random"/> dispatches virtually through a heap object;
/// this is a sealed class whose <see cref="NextUInt64"/> inlines to a handful of shifts,
/// which measurably moves the needle on the step loop.
/// </para>
/// <para>
/// It is also explicitly seedable and reproducible, which is the whole point of the
/// <c>seed</c> parameter on <c>IEnvironment.Reset</c>: the same seed must replay the same
/// episode.
/// </para>
/// </remarks>
public sealed class FastRandom
{
    private ulong _s0, _s1, _s2, _s3;

    // Cached second normal deviate: the polar method produces two at a time and throwing one
    // away doubles the cost of every Gaussian draw, which matters for SAC/TD3 exploration.
    private float _spareGaussian;
    private bool _hasSpareGaussian;

    /// <summary>Creates a generator seeded from a time-and-identity mix.</summary>
    public FastRandom() : this(Environment.TickCount64 ^ ((long)System.Random.Shared.Next() << 20)) { }

    /// <summary>Creates a generator with an explicit seed.</summary>
    public FastRandom(long seed) => Seed(seed);

    /// <summary>Re-seeds the generator in place, discarding any cached Gaussian.</summary>
    public void Seed(long seed)
    {
        // SplitMix64 expansion: xoshiro is badly behaved when seeded with a mostly-zero state,
        // so the 64-bit seed is stretched through a separate mixer first.
        ulong x = (ulong)seed;
        _s0 = SplitMix(ref x);
        _s1 = SplitMix(ref x);
        _s2 = SplitMix(ref x);
        _s3 = SplitMix(ref x);
        _hasSpareGaussian = false;
    }

    private static ulong SplitMix(ref ulong x)
    {
        ulong z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Returns the next raw 64-bit value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong NextUInt64()
    {
        ulong result = BitOperations.RotateLeft(_s0 + _s3, 23) + _s0;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>Returns a uniform value in [0, 1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextSingle() => (NextUInt64() >> 40) * (1.0f / (1 << 24));

    /// <summary>Returns a uniform value in [0, 1) at double precision.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Returns a uniform integer in [0, <paramref name="exclusiveMax"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int NextInt(int exclusiveMax)
    {
        // Lemire's multiply-shift: one 64x64 multiply instead of a modulo, and unbiased enough
        // for every use here (replay indices, action sampling, tie-breaking).
        ulong m = (ulong)(uint)exclusiveMax * (NextUInt64() >> 32);
        return (int)(m >> 32);
    }

    /// <summary>Returns a uniform integer in [<paramref name="min"/>, <paramref name="exclusiveMax"/>).</summary>
    public int NextInt(int min, int exclusiveMax) => min + NextInt(exclusiveMax - min);

    /// <summary>Returns a uniform value in [<paramref name="min"/>, <paramref name="max"/>).</summary>
    public float NextRange(float min, float max) => min + (max - min) * NextSingle();

    /// <summary>Returns a standard normal deviate (mean 0, standard deviation 1).</summary>
    public float NextGaussian()
    {
        if (_hasSpareGaussian)
        {
            _hasSpareGaussian = false;
            return _spareGaussian;
        }

        // Marsaglia polar form: rejection-samples the unit disc, avoiding the sin/cos pair.
        float u, v, s;
        do
        {
            u = NextSingle() * 2f - 1f;
            v = NextSingle() * 2f - 1f;
            s = u * u + v * v;
        }
        while (s >= 1f || s == 0f);

        float scale = MathF.Sqrt(-2f * MathF.Log(s) / s);
        _spareGaussian = v * scale;
        _hasSpareGaussian = true;
        return u * scale;
    }

    /// <summary>Returns a normal deviate with the given mean and standard deviation.</summary>
    public float NextGaussian(float mean, float stdDev) => mean + stdDev * NextGaussian();

    /// <summary>Samples an index from unnormalised non-negative weights.</summary>
    public int SampleCategorical(ReadOnlySpan<float> weights)
    {
        float total = 0f;
        for (int i = 0; i < weights.Length; i++) total += weights[i];

        float target = NextSingle() * total;
        float running = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            running += weights[i];
            if (running >= target) return i;
        }
        return weights.Length - 1; // only reachable through float rounding at the tail
    }

    /// <summary>Shuffles a span in place (Fisher-Yates).</summary>
    public void Shuffle<T>(Span<T> items)
    {
        for (int i = items.Length - 1; i > 0; i--)
        {
            int j = NextInt(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
