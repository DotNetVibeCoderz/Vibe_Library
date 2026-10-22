// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Buffers;
using RLNet.Neural;
using RLNet.Persistence;
using RLNet.Spaces;
using RLNet.Utils;

namespace RLNet.Agents;

/// <summary>Tunable settings for <see cref="Td3Agent"/>.</summary>
public sealed class Td3Options
{
    /// <summary>Hidden layer widths, used for the actor and both critics.</summary>
    public int[] HiddenSizes { get; set; } = [256, 256];

    /// <summary>Adam step size.</summary>
    public float LearningRate { get; set; } = 3e-4f;

    /// <summary>Discount factor.</summary>
    public float Gamma { get; set; } = 0.99f;

    /// <summary>Polyak coefficient for the target networks.</summary>
    public float Tau { get; set; } = 0.005f;

    /// <summary>Transitions per gradient step.</summary>
    public int BatchSize { get; set; } = 256;

    /// <summary>Transitions collected — by a uniformly random policy — before learning begins.</summary>
    public int LearningStarts { get; set; } = 1_000;

    /// <summary>Environment steps between gradient steps.</summary>
    public int TrainFrequency { get; set; } = 1;

    /// <summary>Replay capacity used when the agent builds its own buffer.</summary>
    public int BufferCapacity { get; set; } = 200_000;

    /// <summary>Standard deviation of the Gaussian noise added to actions during collection.</summary>
    public float ExplorationNoise { get; set; } = 0.1f;

    /// <summary>Standard deviation of the smoothing noise added to target actions.</summary>
    public float PolicyNoise { get; set; } = 0.2f;

    /// <summary>Bound on the target smoothing noise, so a single draw cannot move the target far.</summary>
    public float NoiseClip { get; set; } = 0.5f;

    /// <summary>Critic updates per actor update.</summary>
    public int PolicyDelay { get; set; } = 2;

    /// <summary>Global gradient-norm clip, or 0 to disable.</summary>
    public float MaxGradientNorm { get; set; } = 10f;
}

/// <summary>
/// Twin Delayed DDPG: off-policy continuous control with a deterministic policy.
/// </summary>
/// <remarks>
/// <para>
/// Fujimoto et al. (2018). DDPG works when it works and diverges when it does not, and TD3 is
/// three specific fixes for why. <b>Twin critics</b> (in <see cref="QNetworkPair"/>) replace the
/// overestimating single critic with a pessimistic minimum. <b>Delayed policy updates</b> let the
/// critics settle for a couple of steps before the actor chases them, so the actor is not
/// climbing a surface that is still moving under it. <b>Target policy smoothing</b> adds clipped
/// noise to the successor action, which stops the actor exploiting a narrow spike where the
/// critic happens to be wrong — it forces the critic to be right over a neighbourhood, not a point.
/// </para>
/// <para>
/// Against <see cref="SacAgent"/>: TD3's policy is deterministic, so exploration is injected
/// noise with a magnitude the user has to choose, where SAC learns its own. TD3 is often the
/// stronger of the two once tuned, and SAC is far more likely to work on the first attempt.
/// </para>
/// </remarks>
public sealed class Td3Agent : IVectorContinuousAgent, ICheckpointable
{
    private readonly Td3Options _options;
    private readonly BoxSpace _actionSpace;
    private readonly IReplayBuffer _buffer;
    private readonly ReplayBatch _batch;
    private readonly FastRandom _random;

    private readonly MlpNetwork _actor;
    private readonly MlpNetwork _actorTarget;
    private readonly AdamOptimizer _actorOptimizer;
    private readonly QNetworkPair _critics;

    private readonly int _observationSize;
    private readonly int _actionSize;

    private readonly float[] _targetActions;
    private readonly float[] _targets;
    private readonly float[] _tdErrors;
    private readonly float[] _policyActions;

    private int _stepsSinceTrain;
    private int _updatesSinceActor;

    public Td3Agent(
        Space observationSpace,
        BoxSpace actionSpace,
        Td3Options? options = null,
        IReplayBuffer? buffer = null,
        int? seed = null,
        IComputeBackend? backend = null)
    {
        _options = options ?? new Td3Options();
        _actionSpace = actionSpace;
        _random = seed.HasValue ? new FastRandom(seed.Value) : new FastRandom();

        _observationSize = observationSpace.FlatSize;
        _actionSize = actionSpace.FlatSize;

        _buffer = buffer ?? new UniformReplayBuffer(_options.BufferCapacity, _observationSize, _actionSize);
        _batch = new ReplayBatch(_options.BatchSize, _observationSize, _actionSize);

        // A tanh output head puts actions in [-1, 1] by construction, so the actor can never
        // propose something the environment has to clamp away.
        _actor = new MlpNetwork(
            _observationSize, _options.HiddenSizes, _actionSize,
            Activation.ReLU, Activation.Tanh,
            _options.BatchSize, _random, 1f, backend);

        _actorTarget = new MlpNetwork(
            _observationSize, _options.HiddenSizes, _actionSize,
            Activation.ReLU, Activation.Tanh,
            _options.BatchSize, _random, 1f, backend);

        _actorTarget.CopyFrom(_actor);

        _actorOptimizer = new AdamOptimizer(
            _actor.ParameterCount, _options.LearningRate, maxGradientNorm: _options.MaxGradientNorm);

        _critics = new QNetworkPair(
            _observationSize, _actionSize, _options.HiddenSizes,
            _options.LearningRate, _options.BatchSize, _random,
            _options.MaxGradientNorm, backend);

        _targetActions = new float[_options.BatchSize * _actionSize];
        _targets = new float[_options.BatchSize];
        _tdErrors = new float[_options.BatchSize];
        _policyActions = new float[_options.BatchSize * _actionSize];
    }

    public string Name => "TD3";
    public AgentMetrics Metrics { get; } = new();

    /// <summary>The replay buffer in use.</summary>
    public IReplayBuffer Buffer => _buffer;

    /// <summary>TD3 runs at a constant learning rate; progress is not used.</summary>
    public void SetProgress(float progress) { }

    public void SelectAction(ReadOnlySpan<float> observation, Span<float> action, bool deterministic = false) =>
        SelectActions(observation, action, deterministic);

    public void SelectActions(ReadOnlySpan<float> observations, Span<float> actions, bool deterministic = false)
    {
        int count = actions.Length / _actionSize;
        if (actions.Length != count * _actionSize || observations.Length != count * _observationSize)
            throw new ArgumentException(
                $"Expected [count, {_observationSize}] observations and [count, {_actionSize}] actions.", nameof(actions));

        if (!deterministic && Metrics.StepCount < _options.LearningStarts)
        {
            for (int i = 0; i < actions.Length; i++) actions[i] = _random.NextRange(-1f, 1f);
        }
        else
        {
            for (int start = 0; start < count; start += _actor.MaxBatch)
            {
                int batch = Math.Min(_actor.MaxBatch, count - start);
                observations.Slice(start * _observationSize, batch * _observationSize).CopyTo(_actor.InputBuffer(batch));
                var outputs = _actor.Forward(batch);

                for (int i = 0; i < batch * _actionSize; i++)
                {
                    float value = outputs[i];

                    // The policy is deterministic, so all exploration is this noise. Its magnitude
                    // is the one hyper-parameter TD3 is genuinely sensitive to.
                    if (!deterministic) value += _random.NextGaussian() * _options.ExplorationNoise;

                    actions[start * _actionSize + i] = Math.Clamp(value, -1f, 1f);
                }
            }
        }

        for (int row = 0; row < count; row++)
            _actionSpace.ScaleFromUnit(actions.Slice(row * _actionSize, _actionSize));
    }

    public void Observe(
        ReadOnlySpan<float> observation,
        ReadOnlySpan<float> action,
        float reward,
        ReadOnlySpan<float> nextObservation,
        bool terminated,
        bool truncated)
    {
        Span<float> unit = stackalloc float[_actionSize];
        for (int i = 0; i < _actionSize; i++)
        {
            float low = _actionSpace.Low[i], high = _actionSpace.High[i];
            float mid = (high + low) * 0.5f, halfRange = (high - low) * 0.5f;
            unit[i] = halfRange > 0f ? Math.Clamp((action[i] - mid) / halfRange, -1f, 1f) : 0f;
        }

        _buffer.Add(observation, unit, reward, nextObservation, terminated);
        Metrics.StepCount++;

        if (_buffer.Count < _options.LearningStarts) return;
        if (++_stepsSinceTrain < _options.TrainFrequency) return;

        _stepsSinceTrain = 0;
        Train();
    }

    /// <summary>Hands the agent one transition per environment copy.</summary>
    /// <remarks>
    /// One transition at a time, in copy order, exactly as <see cref="Observe"/> would take them —
    /// so <c>TrainFrequency</c> counts transitions, not vector steps, and the same options do the
    /// same number of gradient steps per sample whether the experience came from one copy or eight.
    /// </remarks>
    public void ObserveBatch(
        ReadOnlySpan<float> observations,
        ReadOnlySpan<float> actions,
        ReadOnlySpan<float> rewards,
        ReadOnlySpan<float> nextObservations,
        ReadOnlySpan<bool> terminated,
        ReadOnlySpan<bool> truncated)
    {
        for (int i = 0; i < rewards.Length; i++)
        {
            Observe(
                observations.Slice(i * _observationSize, _observationSize),
                actions.Slice(i * _actionSize, _actionSize), rewards[i],
                nextObservations.Slice(i * _observationSize, _observationSize), terminated[i], truncated[i]);
        }
    }

    public void OnEpisodeEnd() { }

    private void Train()
    {
        _buffer.Sample(_options.BatchSize, _batch, _random);
        int batch = _batch.Count;

        // --- Target actions, with smoothing --------------------------------------------------

        _batch.NextObservations.AsSpan(0, batch * _observationSize).CopyTo(_actorTarget.InputBuffer(batch));
        var targetOutput = _actorTarget.Forward(batch);

        for (int i = 0; i < batch * _actionSize; i++)
        {
            // Clipped noise, not raw noise. An unbounded draw would occasionally propose an
            // action far from the policy's, and the target would then be scored somewhere the
            // policy will never actually visit.
            float noise = Math.Clamp(
                _random.NextGaussian() * _options.PolicyNoise,
                -_options.NoiseClip, _options.NoiseClip);

            _targetActions[i] = Math.Clamp(targetOutput[i] + noise, -1f, 1f);
        }

        // --- Critics -------------------------------------------------------------------------

        _critics.EvaluateTargetMinimum(_batch.NextObservations, _targetActions, batch, _targets.AsSpan(0, batch));

        for (int i = 0; i < batch; i++)
        {
            _targets[i] = _batch.Terminated[i]
                ? _batch.Rewards[i]
                : _batch.Rewards[i] + _options.Gamma * _targets[i];
        }

        float criticLoss = _critics.Update(
            _batch.Observations, _batch.Actions, _targets.AsSpan(0, batch), batch,
            _batch.Weights.AsSpan(0, batch), _tdErrors.AsSpan(0, batch));

        _buffer.UpdatePriorities(_batch.Indices.AsSpan(0, batch), _tdErrors.AsSpan(0, batch));

        Metrics.ValueLoss = criticLoss;
        Metrics.UpdateCount++;

        // --- Actor, on a delay ---------------------------------------------------------------

        if (++_updatesSinceActor < _options.PolicyDelay) return;
        _updatesSinceActor = 0;

        _batch.Observations.AsSpan(0, batch * _observationSize).CopyTo(_actor.InputBuffer(batch));
        var actions = _actor.Forward(batch);
        actions[..(batch * _actionSize)].CopyTo(_policyActions);

        var actionGradient = _critics.ActionGradientOfMinimum(_batch.Observations, _policyActions, batch);

        // No second forward pass for the actor. The critics are separate networks with their own
        // activation caches, so evaluating them above left the actor's cache from the pass just
        // above intact and ready to backpropagate through.
        var gradient = _actor.OutputGradientBuffer(batch);

        // The actor maximises Q, so its loss gradient is the negative of the critic's action
        // gradient. The tanh derivative is applied by the layer itself during backpropagation,
        // since the output head's activation is part of the network.
        for (int i = 0; i < batch * _actionSize; i++)
            gradient[i] = -actionGradient[i] / batch;

        _actor.ZeroGradients();
        _actor.Backward(batch);
        _actor.ApplyGradients(_actorOptimizer);

        _actorTarget.SoftUpdateFrom(_actor, _options.Tau);
        _critics.SoftUpdateTargets(_options.Tau);

        float policyLoss = 0f;
        for (int i = 0; i < batch * _actionSize; i++) policyLoss -= actionGradient[i] * _policyActions[i];
        Metrics.PolicyLoss = policyLoss / batch;
    }

    /// <summary>Copies the actor's parameters out, for saving a trained policy.</summary>
    public float[] ExportParameters() => _actor.ExportParameters();

    /// <summary>Loads parameters into both the actor and its target.</summary>
    public void ImportParameters(ReadOnlySpan<float> parameters)
    {
        _actor.ImportParameters(parameters);
        _actorTarget.CopyFrom(_actor);
    }

    /// <summary>
    /// Writes the actor and its target, all four critics, every optimiser and the generator.
    /// The replay buffer is not saved.
    /// </summary>
    public void WriteState(CheckpointWriter writer)
    {
        writer.Write("actor", _actor);
        writer.Write("actorTarget", _actorTarget);
        writer.Write("actorOptimizer", _actorOptimizer);
        writer.WriteComponent("critics", _critics);
        writer.Write("random", _random);
        writer.Write("stepsSinceTrain", _stepsSinceTrain);
        writer.Write("updatesSinceActor", _updatesSinceActor);
        writer.Write("steps", Metrics.StepCount);
        writer.Write("updates", Metrics.UpdateCount);
    }

    public void ReadState(CheckpointReader reader)
    {
        reader.Read("actor", _actor);
        reader.Read("actorTarget", _actorTarget);
        reader.Read("actorOptimizer", _actorOptimizer);
        reader.ReadComponent("critics", _critics);
        reader.Read("random", _random);
        _stepsSinceTrain = (int)reader.ReadInt64("stepsSinceTrain");
        _updatesSinceActor = (int)reader.ReadInt64("updatesSinceActor");
        Metrics.StepCount = reader.ReadInt64("steps");
        Metrics.UpdateCount = reader.ReadInt64("updates");
    }
}
