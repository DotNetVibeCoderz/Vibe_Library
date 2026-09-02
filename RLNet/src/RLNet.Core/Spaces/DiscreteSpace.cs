// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Utils;

namespace RLNet.Spaces;

/// <summary>A finite set of actions numbered <c>0 .. Count-1</c>.</summary>
public sealed class DiscreteSpace : Space
{
    /// <summary>Number of distinct values.</summary>
    public int Count { get; }

    /// <summary>Optional names for each value, used by the visualizer to label the action.</summary>
    public IReadOnlyList<string>? Labels { get; }

    public DiscreteSpace(int count, IReadOnlyList<string>? labels = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        if (labels is not null && labels.Count != count)
            throw new ArgumentException($"Expected {count} labels but got {labels.Count}.", nameof(labels));

        Count = count;
        Labels = labels;
    }

    /// <summary>A discrete sample is a single index, so it flattens to one slot.</summary>
    public override int FlatSize => 1;

    /// <summary>Returns a uniformly random action index.</summary>
    public int SampleIndex(FastRandom random) => random.NextInt(Count);

    public override void Sample(FastRandom random, Span<float> destination) =>
        destination[0] = SampleIndex(random);

    public override bool Contains(ReadOnlySpan<float> value) =>
        value.Length == 1 && value[0] >= 0 && value[0] < Count && value[0] == MathF.Floor(value[0]);

    /// <summary>Returns the label for an action, falling back to its index.</summary>
    public string LabelOf(int index) =>
        Labels is not null && (uint)index < (uint)Labels.Count ? Labels[index] : index.ToString();

    public override string ToString() => $"Discrete({Count})";
}
