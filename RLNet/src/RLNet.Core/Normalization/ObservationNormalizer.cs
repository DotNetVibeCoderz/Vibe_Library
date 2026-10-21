// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Persistence;

namespace RLNet.Normalization;

/// <summary>
/// Standardises observations to roughly zero mean and unit variance using running statistics.
/// </summary>
/// <remarks>
/// <para>
/// The environments here already keep their observations near <c>[-1, 1]</c>, but an environment
/// written outside the library often does not — a price in the thousands next to a flag that is 0
/// or 1 — and a network's first layer then sees one input dominate every gradient. Running
/// statistics fix that without the environment author having to know the ranges up front.
/// </para>
/// <para>
/// The statistics are part of the policy: a network trained on standardised inputs is meaningless
/// on raw ones. That is why this lives inside the agent and is saved in its checkpoint, rather
/// than in an environment wrapper that would have to be shipped and restored separately.
/// </para>
/// </remarks>
public sealed class ObservationNormalizer : ICheckpointable
{
    private readonly RunningMeanStd _statistics;

    /// <param name="dimensions">Observation width.</param>
    /// <param name="clip">Standardised values are clipped to <c>±clip</c>, bounding the effect of an outlier.</param>
    public ObservationNormalizer(int dimensions, float clip = 10f)
    {
        _statistics = new RunningMeanStd(dimensions);
        Clip = clip;
    }

    /// <summary>Bound on the standardised value.</summary>
    public float Clip { get; }

    /// <summary>The underlying statistics.</summary>
    public RunningMeanStd Statistics => _statistics;

    /// <summary>Folds a raw observation into the statistics.</summary>
    public void Update(ReadOnlySpan<float> observation) => _statistics.Update(observation);

    /// <summary>Writes the standardised form of <paramref name="observation"/> into <paramref name="destination"/>.</summary>
    public void Normalize(ReadOnlySpan<float> observation, Span<float> destination)
    {
        for (int i = 0; i < observation.Length; i++)
        {
            float std = MathF.Sqrt(_statistics.Variance(i) + 1e-8f);
            destination[i] = Math.Clamp((observation[i] - _statistics.Mean(i)) / std, -Clip, Clip);
        }
    }

    public void WriteState(CheckpointWriter writer) => _statistics.WriteState(writer);

    public void ReadState(CheckpointReader reader) => _statistics.ReadState(reader);
}
