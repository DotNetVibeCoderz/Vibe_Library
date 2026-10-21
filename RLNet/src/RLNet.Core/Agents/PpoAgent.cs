// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Neural;
using RLNet.Policies;
using RLNet.Spaces;

namespace RLNet.Agents;

/// <summary>Tunable settings for <see cref="PpoAgent"/>.</summary>
public sealed class PpoOptions
{
    /// <summary>Hidden layer widths, used for both the actor and the critic.</summary>
    public int[] HiddenSizes { get; set; } = [64, 64];

    /// <summary>Adam step size.</summary>
    public float LearningRate { get; set; } = 3e-4f;

    /// <summary>Discount factor.</summary>
    public float Gamma { get; set; } = 0.99f;

    /// <summary>GAE bias-variance dial.</summary>
    public float GaeLambda { get; set; } = 0.95f;

    /// <summary>Steps collected before each update.</summary>
    public int RolloutLength { get; set; } = 2_048;

    /// <summary>Samples per gradient step.</summary>
    public int MinibatchSize { get; set; } = 64;

    /// <summary>Passes over each rollout.</summary>
    public int Epochs { get; set; } = 10;

    /// <summary>Trust-region width. The policy ratio is clipped to <c>[1-ε, 1+ε]</c>.</summary>
    public float ClipRange { get; set; } = 0.2f;

    /// <summary>Weight on the value loss.</summary>
    public float ValueCoefficient { get; set; } = 0.5f;

    /// <summary>Weight on the entropy bonus.</summary>
    public float EntropyCoefficient { get; set; } = 0.01f;

    /// <summary>Global gradient-norm clip.</summary>
    public float MaxGradientNorm { get; set; } = 0.5f;

    /// <summary>Standardise advantages within each minibatch.</summary>
    public bool NormalizeAdvantages { get; set; } = true;

    /// <summary>
    /// Decay the learning rate linearly to zero over training.
    /// </summary>
    /// <remarks>
    /// Standard in the reference implementations, and it needs training progress to be reported
    /// through <see cref="IAgent.SetProgress"/> — which <see cref="Training.Trainer"/> does. Left
    /// to itself the agent simply trains at a constant rate.
    /// </remarks>
    public bool AnnealLearningRate { get; set; } = true;

    /// <summary>
    /// Stop an update early once the mean KL from the collecting policy passes this, or 0 to
    /// disable. A safety net for when the clip alone fails to hold the step in.
    /// </summary>
    public float TargetKl { get; set; } = 0.02f;

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

    internal OnPolicySettings ToSettings() => new(
        OnPolicyMethod.Ppo, HiddenSizes, LearningRate, Gamma, GaeLambda, RolloutLength,
        MinibatchSize, Epochs, ClipRange, ValueCoefficient, EntropyCoefficient, MaxGradientNorm,
        NormalizeAdvantages, AnnealLearningRate, TargetKl, NormalizeObservations, NormalizeRewards);
}

/// <summary>
/// Proximal Policy Optimization: on-policy learning with a clipped trust region.
/// </summary>
/// <remarks>
/// <para>
/// Schulman et al. (2017), and the default worth reaching for first on an unfamiliar discrete
/// task. Its appeal is not peak performance but that it works across a wide range of problems
/// without per-task tuning.
/// </para>
/// <para>
/// The idea it is built on: a policy-gradient step is only valid near the policy that collected
/// the data, and a large step invalidates the very samples justifying it. PPO takes several
/// gradient steps per rollout anyway — which is what makes it sample-efficient — and keeps them
/// honest by clipping the probability ratio. Once an action has become more than <c>1+ε</c>
/// times as likely as it was at collection, the objective flattens and its gradient vanishes, so
/// further steps simply cannot push it further. It is a trust region enforced by the shape of
/// the loss rather than by a constraint solver.
/// </para>
/// <para>
/// Actor and critic are separate networks. Sharing a trunk saves parameters but couples the two
/// losses through it, and the value loss — much larger in magnitude — tends to dominate the
/// policy gradient. Separate networks cost more memory and are markedly easier to tune.
/// </para>
/// <para>
/// This class handles a finite action set; <see cref="ContinuousPpoAgent"/> is the same algorithm
/// over a Gaussian policy. Both accept several environments at once through
/// <see cref="IVectorDiscreteAgent"/>, which shares one network pass across all of them.
/// </para>
/// </remarks>
public sealed class PpoAgent : DiscreteOnPolicyAgent
{
    public PpoAgent(
        Space observationSpace,
        DiscreteSpace actionSpace,
        PpoOptions? options = null,
        int? seed = null,
        IComputeBackend? backend = null)
        : base(new OnPolicyLearner(
            (options ?? new PpoOptions()).ToSettings(),
            new CategoricalHead(actionSpace.Count),
            observationSpace.FlatSize, seed, backend))
    {
    }

    public override string Name => "PPO";
}
