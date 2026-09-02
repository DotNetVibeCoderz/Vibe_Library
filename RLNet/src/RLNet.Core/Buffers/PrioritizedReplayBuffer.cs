// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Utils;

namespace RLNet.Buffers;

/// <summary>
/// Replay that samples transitions in proportion to how surprising they were, after Schaul et
/// al., <i>Prioritized Experience Replay</i> (2016).
/// </summary>
/// <remarks>
/// <para>
/// Uniform replay spends most of its samples on transitions the agent already predicts
/// perfectly. Prioritising by TD error concentrates the gradient budget where the value function
/// is still wrong, which on sparse-reward environments — GridWorld, LunarLander — is often the
/// difference between learning in a few thousand episodes and not learning at all.
/// </para>
/// <para>
/// Sampling non-uniformly biases the expected update, so it has to be paid back: each sample
/// carries an importance-sampling weight of <c>(N * P(i))^-beta</c>, normalised by the largest
/// weight in the buffer. <see cref="Beta"/> is annealed toward 1 over training, which
/// leaves the correction weak while the estimates are noisy and complete by the time they matter.
/// </para>
/// </remarks>
public sealed class PrioritizedReplayBuffer : UniformReplayBuffer
{
    private readonly SumTree _priorities;
    private readonly float _alpha;
    private readonly float _epsilon;

    /// <summary>
    /// Importance-sampling exponent, from 0 (no correction) to 1 (full correction). Raise this
    /// toward 1 over the course of training; <see cref="AnnealBeta"/> does it on a schedule.
    /// </summary>
    public float Beta { get; set; }

    /// <param name="alpha">
    /// How sharply priority follows TD error. 0 degenerates to uniform sampling, 1 samples
    /// strictly in proportion to error; 0.6 is the value the paper settles on.
    /// </param>
    /// <param name="beta">Initial importance-sampling exponent.</param>
    /// <param name="epsilon">Floor added to every priority so a zero-error transition is still reachable.</param>
    public PrioritizedReplayBuffer(
        int capacity,
        int observationSize,
        int actionSize,
        float alpha = 0.6f,
        float beta = 0.4f,
        float epsilon = 1e-3f)
        : base(capacity, observationSize, actionSize)
    {
        _priorities = new SumTree(capacity);
        _alpha = alpha;
        _epsilon = epsilon;
        Beta = beta;
    }

    /// <summary>
    /// A new transition has no TD error yet, so it enters at the highest priority in the buffer.
    /// That guarantees every transition is replayed at least once before it can be down-ranked.
    /// </summary>
    protected override void OnAdded(int slot) =>
        _priorities.Set(slot, _priorities.Max > 0f ? _priorities.Max : 1f);

    public override void Sample(int batchSize, ReplayBatch batch, FastRandom random)
    {
        if (Count == 0) throw new InvalidOperationException("Cannot sample an empty replay buffer.");

        int n = Math.Min(batchSize, batch.Capacity);
        float total = _priorities.Total;

        // Stratified sampling: one draw from each of n equal segments of the cumulative
        // distribution, rather than n independent draws. Same expectation, far lower variance,
        // and it guarantees the batch spans the whole priority range instead of piling onto the
        // few highest-error transitions.
        float segment = total / n;

        // The largest IS weight belongs to the least likely transition, so normalising by it
        // keeps every weight in (0, 1] and the update scale-free. The minimum comes off the tree
        // in O(1); scanning for it would reintroduce the linear cost the tree exists to remove.
        float minProbability = _priorities.Min / total;
        float maxWeight = MathF.Pow(Count * minProbability, -Beta);

        for (int i = 0; i < n; i++)
        {
            float target = random.NextRange(segment * i, segment * (i + 1));
            int slot = _priorities.Find(target);
            if (slot >= Count) slot = Count - 1;

            CopyInto(slot, i, batch);
            batch.Indices[i] = slot;

            float probability = _priorities[slot] / total;
            batch.Weights[i] = MathF.Pow(Count * probability, -Beta) / maxWeight;
        }

        batch.Count = n;
    }

    public override void UpdatePriorities(ReadOnlySpan<int> indices, ReadOnlySpan<float> tdErrors)
    {
        for (int i = 0; i < indices.Length; i++)
        {
            float priority = MathF.Pow(MathF.Abs(tdErrors[i]) + _epsilon, _alpha);
            _priorities.Set(indices[i], priority);
        }
    }

    /// <summary>Moves <see cref="Beta"/> linearly toward 1 given training progress in <c>[0, 1]</c>.</summary>
    public void AnnealBeta(float progress, float initialBeta = 0.4f) =>
        Beta = initialBeta + (1f - initialBeta) * Math.Clamp(progress, 0f, 1f);

    public override void Clear()
    {
        base.Clear();
        _priorities.Clear();
    }
}
