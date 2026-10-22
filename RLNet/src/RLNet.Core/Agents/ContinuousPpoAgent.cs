// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Neural;
using RLNet.Policies;
using RLNet.Spaces;

namespace RLNet.Agents;

/// <summary>
/// Proximal Policy Optimization over continuous actions, with a diagonal Gaussian policy.
/// </summary>
/// <remarks>
/// <para>
/// The same clipped objective, rollout and critic as <see cref="PpoAgent"/>, with the categorical
/// distribution replaced by <see cref="GaussianPolicy"/>: the actor network emits a mean per
/// action dimension, and a free log standard deviation per dimension sets the spread.
/// </para>
/// <para>
/// Where SAC and TD3 learn off-policy from a replay buffer, this learns only from what the current
/// policy just did. It needs more environment steps as a result, and pays that back in stability,
/// in memory — no buffer — and in how well it scales across parallel environments. Continuous
/// control is where reward scale varies most between tasks, so <see cref="PpoOptions.NormalizeRewards"/>
/// is worth turning on here; <see cref="Catalog"/> does.
/// </para>
/// </remarks>
public sealed class ContinuousPpoAgent : ContinuousOnPolicyAgent
{
    public ContinuousPpoAgent(
        Space observationSpace,
        BoxSpace actionSpace,
        PpoOptions? options = null,
        int? seed = null,
        IComputeBackend? backend = null)
        : this(options ?? new PpoOptions(), observationSpace, actionSpace, seed, backend)
    {
    }

    private ContinuousPpoAgent(PpoOptions options, Space observationSpace, BoxSpace actionSpace, int? seed, IComputeBackend? backend)
        : base(new OnPolicyLearner(
            options.ToSettings(),
            // Fixed spread only. gSDE draws its exploration once per rollout, and one draw held
            // for PPO's 2,048 steps explores a single direction: measured, it took Pendulum from
            // -164 to -799. It needs a resample interval first; see PLAN.md.
            ContinuousHead(stateDependent: false, actionSpace, observationSpace, options.HiddenSizes,
                           options.InitialLogStd, options.LearningRate),
            observationSpace.FlatSize, seed, backend), actionSpace)
    {
    }

    public override string Name => "PPO";
}
