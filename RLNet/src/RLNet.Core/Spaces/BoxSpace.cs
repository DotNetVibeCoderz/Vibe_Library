// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Utils;

namespace RLNet.Spaces;

/// <summary>A bounded continuous vector space, one <c>[low, high]</c> interval per dimension.</summary>
public sealed class BoxSpace : Space
{
    private readonly float[] _low;
    private readonly float[] _high;

    /// <summary>Per-dimension lower bounds.</summary>
    public ReadOnlySpan<float> Low => _low;

    /// <summary>Per-dimension upper bounds.</summary>
    public ReadOnlySpan<float> High => _high;

    /// <summary>Optional names for each dimension, used to label the visualizer's readouts.</summary>
    public IReadOnlyList<string>? Labels { get; }

    public BoxSpace(float[] low, float[] high, IReadOnlyList<string>? labels = null)
    {
        if (low.Length != high.Length)
            throw new ArgumentException("low and high must have the same length.", nameof(high));
        if (labels is not null && labels.Count != low.Length)
            throw new ArgumentException($"Expected {low.Length} labels but got {labels.Count}.", nameof(labels));

        _low = low;
        _high = high;
        Labels = labels;
    }

    /// <summary>Creates a space where every dimension shares the same bounds.</summary>
    public static BoxSpace Uniform(int dimensions, float low, float high, IReadOnlyList<string>? labels = null)
    {
        var lo = new float[dimensions];
        var hi = new float[dimensions];
        lo.AsSpan().Fill(low);
        hi.AsSpan().Fill(high);
        return new BoxSpace(lo, hi, labels);
    }

    /// <summary>Creates an unbounded space, for observations with no natural range.</summary>
    public static BoxSpace Unbounded(int dimensions, IReadOnlyList<string>? labels = null) =>
        Uniform(dimensions, float.NegativeInfinity, float.PositiveInfinity, labels);

    public override int FlatSize => _low.Length;

    public override void Sample(FastRandom random, Span<float> destination)
    {
        for (int i = 0; i < _low.Length; i++)
        {
            // An unbounded dimension has no uniform distribution; a standard normal is the
            // conventional stand-in and keeps Sample() total rather than throwing.
            destination[i] = float.IsInfinity(_low[i]) || float.IsInfinity(_high[i])
                ? random.NextGaussian()
                : random.NextRange(_low[i], _high[i]);
        }
    }

    public override bool Contains(ReadOnlySpan<float> value)
    {
        if (value.Length != _low.Length) return false;
        for (int i = 0; i < value.Length; i++)
            if (value[i] < _low[i] || value[i] > _high[i]) return false;
        return true;
    }

    /// <summary>Clamps a value into the space in place. Continuous agents call this before stepping.</summary>
    public void Clamp(Span<float> value)
    {
        for (int i = 0; i < value.Length; i++)
            value[i] = Math.Clamp(value[i], _low[i], _high[i]);
    }

    /// <summary>Maps a value in <c>[-1, 1]</c> onto this space's bounds, per dimension.</summary>
    /// <remarks>
    /// SAC and TD3 both emit actions through a <c>tanh</c>, so their raw output is always
    /// <c>[-1, 1]</c>. Rescaling here keeps that squashing detail out of every environment.
    /// </remarks>
    public void ScaleFromUnit(Span<float> value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            float mid = (_high[i] + _low[i]) * 0.5f;
            float halfRange = (_high[i] - _low[i]) * 0.5f;
            value[i] = mid + value[i] * halfRange;
        }
    }

    /// <summary>Returns the label for a dimension, falling back to its index.</summary>
    public string LabelOf(int index) =>
        Labels is not null && (uint)index < (uint)Labels.Count ? Labels[index] : $"[{index}]";

    public override string ToString() => $"Box({_low.Length})";
}
