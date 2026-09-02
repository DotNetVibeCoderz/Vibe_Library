// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Utils;

namespace RLNet.Buffers;

/// <summary>
/// Fixed-length storage for one on-policy rollout, with generalised advantage estimation.
/// </summary>
/// <remarks>
/// <para>
/// On-policy methods cannot use a replay buffer: PPO and A2C estimate a gradient of the
/// <em>current</em> policy, so every sample has to come from that policy. This buffer therefore
/// holds exactly one rollout, is consumed by one update, and is then cleared — a completely
/// different lifetime from <see cref="UniformReplayBuffer"/>, which is why they share no base.
/// </para>
/// <para>
/// It also stores the log-probability and the value estimate <em>at collection time</em>. PPO's
/// ratio is between the current policy and the one that acted, so the behaviour policy's
/// log-probability has to be frozen when the action was taken; recomputing it later would make
/// every ratio identically 1 and silently turn PPO into vanilla policy gradient.
/// </para>
/// </remarks>
public sealed class RolloutBuffer
{
    private int _count;

    public RolloutBuffer(int capacity, int observationSize, int actionSize)
    {
        Capacity = capacity;
        ObservationSize = observationSize;
        ActionSize = actionSize;

        Observations = new float[capacity * observationSize];
        Actions = new float[capacity * actionSize];
        LogProbabilities = new float[capacity];
        Values = new float[capacity];
        Rewards = new float[capacity];
        Advantages = new float[capacity];
        Returns = new float[capacity];
        Terminated = new bool[capacity];
        Truncated = new bool[capacity];
        BootstrapValues = new float[capacity];
    }

    public int Capacity { get; }
    public int Count => _count;
    public int ObservationSize { get; }
    public int ActionSize { get; }

    /// <summary>Whether the buffer has no room left and is ready for an update.</summary>
    public bool IsFull => _count >= Capacity;

    public float[] Observations { get; }
    public float[] Actions { get; }

    /// <summary>Log-probability of each action under the policy that chose it.</summary>
    public float[] LogProbabilities { get; }

    /// <summary>Critic estimate of each visited state, at collection time.</summary>
    public float[] Values { get; }

    public float[] Rewards { get; }

    /// <summary>GAE advantages, valid after <see cref="ComputeAdvantages"/>.</summary>
    public float[] Advantages { get; }

    /// <summary>Value targets, valid after <see cref="ComputeAdvantages"/>.</summary>
    public float[] Returns { get; }

    public bool[] Terminated { get; }
    public bool[] Truncated { get; }

    /// <summary>
    /// Critic estimate of the state a truncated episode was cut off at. Ignored unless the
    /// matching <see cref="Truncated"/> flag is set.
    /// </summary>
    public float[] BootstrapValues { get; }

    /// <summary>Appends one transition.</summary>
    /// <param name="bootstrapValue">
    /// The critic's estimate of the successor state, needed only when
    /// <paramref name="truncated"/> is set. A time-limited episode has a real future whose value
    /// must still be counted; dropping it teaches the agent that the world ends at the step limit.
    /// </param>
    public void Add(
        ReadOnlySpan<float> observation,
        ReadOnlySpan<float> action,
        float logProbability,
        float value,
        float reward,
        bool terminated,
        bool truncated,
        float bootstrapValue = 0f)
    {
        if (_count >= Capacity)
            throw new InvalidOperationException("Rollout buffer is full; call ComputeAdvantages and Clear first.");

        int slot = _count;
        observation.CopyTo(Observations.AsSpan(slot * ObservationSize, ObservationSize));
        action.CopyTo(Actions.AsSpan(slot * ActionSize, ActionSize));

        LogProbabilities[slot] = logProbability;
        Values[slot] = value;
        Rewards[slot] = reward;
        Terminated[slot] = terminated;
        Truncated[slot] = truncated;
        BootstrapValues[slot] = bootstrapValue;

        _count++;
    }

    /// <summary>Convenience overload for discrete actions.</summary>
    public void AddDiscrete(
        ReadOnlySpan<float> observation,
        int action,
        float logProbability,
        float value,
        float reward,
        bool terminated,
        bool truncated,
        float bootstrapValue = 0f)
    {
        Span<float> encoded = stackalloc float[1];
        encoded[0] = action;
        Add(observation, encoded, logProbability, value, reward, terminated, truncated, bootstrapValue);
    }

    /// <summary>
    /// Fills <see cref="Advantages"/> and <see cref="Returns"/> by generalised advantage
    /// estimation, after Schulman et al., <i>High-Dimensional Continuous Control Using GAE</i> (2016).
    /// </summary>
    /// <param name="lastValue">
    /// Critic estimate of the state the rollout stopped at, used to bootstrap the final step when
    /// the rollout was cut mid-episode. Pass 0 if the last step ended the episode.
    /// </param>
    /// <param name="gamma">Discount factor.</param>
    /// <param name="lambda">
    /// Bias-variance dial. At 0 the estimate is the one-step TD error — low variance, high bias.
    /// At 1 it is the full Monte-Carlo return — unbiased, high variance. 0.95 is the usual
    /// compromise and what both PPO and A2C default to here.
    /// </param>
    public void ComputeAdvantages(float lastValue, float gamma, float lambda)
    {
        float runningAdvantage = 0f;

        for (int t = _count - 1; t >= 0; t--)
        {
            // Two distinct questions, and conflating them is the classic GAE bug:
            //   what is the value of the next state?  -> zero only for a REAL terminal state
            //   does the advantage chain continue?    -> no, for termination OR truncation
            float nextValue;
            if (Terminated[t]) nextValue = 0f;
            else if (Truncated[t]) nextValue = BootstrapValues[t];
            else nextValue = t == _count - 1 ? lastValue : Values[t + 1];

            bool chainBroken = Terminated[t] || Truncated[t] || t == _count - 1;
            float nextAdvantage = chainBroken ? 0f : runningAdvantage;

            float delta = Rewards[t] + gamma * nextValue - Values[t];
            runningAdvantage = delta + gamma * lambda * nextAdvantage;

            Advantages[t] = runningAdvantage;

            // The value target is the advantage put back on top of the old estimate, which is
            // the lambda-return. Regressing the critic on this rather than on the raw discounted
            // return inherits GAE's variance reduction.
            Returns[t] = runningAdvantage + Values[t];
        }
    }

    /// <summary>
    /// Standardises the advantages to zero mean and unit variance across the rollout.
    /// </summary>
    /// <remarks>
    /// Not cosmetic. The policy-gradient step size scales with the magnitude of the advantage,
    /// so a reward scale of 1 and a reward scale of 1000 would otherwise need different learning
    /// rates for identical problems. Normalising makes one set of hyper-parameters transfer
    /// across environments, which is most of why PPO is usable without per-task tuning.
    /// </remarks>
    public void NormalizeAdvantages()
    {
        if (_count < 2) return;

        var advantages = Advantages.AsSpan(0, _count);

        float mean = 0f;
        for (int i = 0; i < _count; i++) mean += advantages[i];
        mean /= _count;

        float variance = 0f;
        for (int i = 0; i < _count; i++)
        {
            float d = advantages[i] - mean;
            variance += d * d;
        }
        float invStd = 1f / (MathF.Sqrt(variance / _count) + 1e-8f);

        for (int i = 0; i < _count; i++)
            advantages[i] = (advantages[i] - mean) * invStd;
    }

    /// <summary>
    /// Fills <paramref name="order"/> with a fresh shuffle of <c>0 .. Count-1</c>, for iterating
    /// the rollout in random minibatches across PPO's several epochs.
    /// </summary>
    public void ShuffleIndices(Span<int> order, FastRandom random)
    {
        for (int i = 0; i < _count; i++) order[i] = i;
        random.Shuffle(order[.._count]);
    }

    /// <summary>Empties the buffer for the next rollout.</summary>
    public void Clear() => _count = 0;
}
