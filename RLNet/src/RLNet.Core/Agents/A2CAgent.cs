// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Neural;
using RLNet.Policies;
using RLNet.Spaces;

namespace RLNet.Agents;

/// <summary>Tunable settings for <see cref="A2CAgent"/>.</summary>
public sealed class A2COptions
{
    /// <summary>Hidden layer widths, used for both the actor and the critic.</summary>
    public int[] HiddenSizes { get; set; } = [64, 64];

    /// <summary>Adam step size. Higher than PPO's, since each sample is used exactly once.</summary>
    public float LearningRate { get; set; } = 7e-4f;

    /// <summary>Discount factor.</summary>
    public float Gamma { get; set; } = 0.99f;

    /// <summary>GAE bias-variance dial.</summary>
    public float GaeLambda { get; set; } = 0.95f;

    /// <summary>Steps collected before each update. Short by design.</summary>
    public int RolloutLength { get; set; } = 32;

    /// <summary>Weight on the value loss.</summary>
    public float ValueCoefficient { get; set; } = 0.5f;

    /// <summary>Weight on the entropy bonus.</summary>
    public float EntropyCoefficient { get; set; } = 0.01f;

    /// <summary>Global gradient-norm clip.</summary>
    public float MaxGradientNorm { get; set; } = 0.5f;

    /// <summary>Standardise advantages across the rollout.</summary>
    public bool NormalizeAdvantages { get; set; } = true;

    /// <summary>
    /// Standardise observations with running statistics before they reach either network.
    /// The statistics are saved in the agent's checkpoint.
    /// </summary>
    public bool NormalizeObservations { get; set; }

    /// <summary>Scale rewards by the running spread of the discounted return.</summary>
    public bool NormalizeRewards { get; set; }

    /// <summary>
    /// Starting log standard deviation of the Gaussian policy, continuous actions only. 0 means a
    /// spread of 1 on the unit action scale — wide enough to reach both bounds from the start.
    /// </summary>
    public float InitialLogStd { get; set; }

    /// <summary>
    /// Explore with generalised state-dependent exploration (gSDE) instead of independent
    /// per-step noise — continuous actions only. The perturbation is a function of the state,
    /// redrawn once per rollout, so a trajectory explores one coherent variation of the policy.
    /// <see cref="InitialLogStd"/> then sets the per-feature spread.
    /// </summary>
    /// <remarks>
    /// Train it on several copies of the environment: each copy draws its own perturbation, so
    /// one update sees several coherent variations rather than one. Measured over twelve seeds on
    /// four copies, it held Reacher's mean (−87) while cutting the spread across seeds from ±35 to
    /// ±9, and took Pendulum's median from −568 to −220. On a single environment it is steadier
    /// but scores worse. <see cref="Catalog"/> turns it on, at an initial log spread of −0.5.
    /// </remarks>
    public bool StateDependentExploration { get; set; }

    internal OnPolicySettings ToSettings() => new(
        OnPolicyMethod.A2C, HiddenSizes, LearningRate, Gamma, GaeLambda, RolloutLength,
        MinibatchSize: RolloutLength, Epochs: 1, ClipRange: 0f, ValueCoefficient, EntropyCoefficient,
        MaxGradientNorm, NormalizeAdvantages, AnnealLearningRate: false, TargetKl: 0f,
        NormalizeObservations, NormalizeRewards);
}

/// <summary>
/// Advantage Actor-Critic: one gradient step per short rollout, strictly on-policy.
/// </summary>
/// <remarks>
/// <para>
/// The synchronous form of Mnih et al.'s A3C (2016). Compared with <see cref="PpoAgent"/> the
/// difference is what happens after a rollout: A2C takes exactly one gradient step and throws
/// the data away, so the policy never drifts from the one that collected it and no clipping is
/// needed. That makes it markedly simpler — and markedly less sample-efficient, since every
/// transition contributes to exactly one update.
/// </para>
/// <para>
/// It earns its place for two reasons. It is the shortest complete actor-critic in the library,
/// so it is the one to read first — the update is <c>OnPolicyLearner.UpdateA2C</c>, which shares
/// everything else with PPO; and its short rollout means it updates far more often than
/// PPO, which makes early learning visible within seconds in the visualizer rather than after
/// the first 2048-step rollout completes.
/// </para>
/// <para>
/// <see cref="ContinuousA2CAgent"/> is the same algorithm over a Gaussian policy.
/// </para>
/// </remarks>
public sealed class A2CAgent : DiscreteOnPolicyAgent
{
    public A2CAgent(
        Space observationSpace,
        DiscreteSpace actionSpace,
        A2COptions? options = null,
        int? seed = null,
        IComputeBackend? backend = null)
        : base(new OnPolicyLearner(
            (options ?? new A2COptions()).ToSettings(),
            new CategoricalHead(actionSpace.Count),
            observationSpace.FlatSize, seed, backend))
    {
    }

    public override string Name => "A2C";
}
