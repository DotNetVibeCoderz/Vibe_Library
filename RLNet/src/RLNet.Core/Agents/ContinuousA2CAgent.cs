// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Neural;
using RLNet.Policies;
using RLNet.Spaces;

namespace RLNet.Agents;

/// <summary>
/// Advantage Actor-Critic over continuous actions, with a diagonal Gaussian policy.
/// </summary>
/// <remarks>
/// The continuous counterpart of <see cref="A2CAgent"/>. Plain policy gradient has no trust
/// region, so on continuous control it is sensitive to the learning rate and benefits even more
/// than PPO from reward normalisation and from several parallel environments, which average out
/// the noise in each short rollout.
/// </remarks>
public sealed class ContinuousA2CAgent : ContinuousOnPolicyAgent
{
    public ContinuousA2CAgent(
        Space observationSpace,
        BoxSpace actionSpace,
        A2COptions? options = null,
        int? seed = null,
        IComputeBackend? backend = null)
        : this(options ?? new A2COptions(), observationSpace, actionSpace, seed, backend)
    {
    }

    private ContinuousA2CAgent(A2COptions options, Space observationSpace, BoxSpace actionSpace, int? seed, IComputeBackend? backend)
        : base(new OnPolicyLearner(
            options.ToSettings(),
            new GaussianHead(actionSpace.FlatSize, options.InitialLogStd, options.LearningRate),
            observationSpace.FlatSize, seed, backend), actionSpace)
    {
    }

    public override string Name => "A2C";
}
