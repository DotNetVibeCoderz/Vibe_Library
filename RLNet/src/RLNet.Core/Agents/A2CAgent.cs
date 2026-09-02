// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Buffers;
using RLNet.Neural;
using RLNet.Policies;
using RLNet.Spaces;
using RLNet.Utils;

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
/// so it is the one to read first; and its short rollout means it updates far more often than
/// PPO, which makes early learning visible within seconds in the visualizer rather than after
/// the first 2048-step rollout completes.
/// </para>
/// </remarks>
public sealed class A2CAgent : IDiscreteAgent
{
    private readonly A2COptions _options;
    private readonly RolloutBuffer _rollout;
    private readonly FastRandom _random;

    private readonly MlpNetwork _actor;
    private readonly MlpNetwork _critic;
    private readonly AdamOptimizer _actorOptimizer;
    private readonly AdamOptimizer _criticOptimizer;

    private readonly int _actionCount;
    private readonly int _observationSize;
    private readonly float[] _probabilities;

    private float _pendingLogProbability;
    private float _pendingValue;

    public A2CAgent(
        Space observationSpace,
        DiscreteSpace actionSpace,
        A2COptions? options = null,
        int? seed = null,
        IComputeBackend? backend = null)
    {
        _options = options ?? new A2COptions();
        _random = seed.HasValue ? new FastRandom(seed.Value) : new FastRandom();

        _observationSize = observationSpace.FlatSize;
        _actionCount = actionSpace.Count;

        _rollout = new RolloutBuffer(_options.RolloutLength, _observationSize, 1);

        _actor = new MlpNetwork(
            _observationSize, _options.HiddenSizes, _actionCount,
            Activation.Tanh, Activation.Linear,
            _options.RolloutLength, _random, outputScale: 0.01f, backend);

        _critic = new MlpNetwork(
            _observationSize, _options.HiddenSizes, 1,
            Activation.Tanh, Activation.Linear,
            _options.RolloutLength, _random, 1f, backend);

        _actorOptimizer = new AdamOptimizer(
            _actor.ParameterCount, _options.LearningRate, maxGradientNorm: _options.MaxGradientNorm);
        _criticOptimizer = new AdamOptimizer(
            _critic.ParameterCount, _options.LearningRate, maxGradientNorm: _options.MaxGradientNorm);

        _probabilities = new float[_actionCount];
    }

    public string Name => "A2C";
    public AgentMetrics Metrics { get; } = new();

    /// <summary>A2C runs at a constant learning rate; progress is not used.</summary>
    public void SetProgress(float progress) { }

    public int SelectAction(ReadOnlySpan<float> observation, bool deterministic = false)
    {
        var logits = _actor.Forward(observation);
        int action = CategoricalPolicy.Sample(
            logits, _probabilities, _random, deterministic, out _pendingLogProbability);

        _pendingValue = _critic.Forward(observation)[0];
        Metrics.Entropy = CategoricalPolicy.Entropy(_probabilities);

        return action;
    }

    public void Observe(
        ReadOnlySpan<float> observation,
        int action,
        float reward,
        ReadOnlySpan<float> nextObservation,
        bool terminated,
        bool truncated)
    {
        float bootstrap = truncated ? _critic.Forward(nextObservation)[0] : 0f;

        _rollout.AddDiscrete(
            observation, action, _pendingLogProbability, _pendingValue,
            reward, terminated, truncated, bootstrap);

        Metrics.StepCount++;

        if (_rollout.IsFull)
        {
            bool episodeContinues = !terminated && !truncated;
            float lastValue = episodeContinues ? _critic.Forward(nextObservation)[0] : 0f;
            Update(lastValue);
        }
    }

    public void OnEpisodeEnd() { }

    private void Update(float lastValue)
    {
        _rollout.ComputeAdvantages(lastValue, _options.Gamma, _options.GaeLambda);
        if (_options.NormalizeAdvantages) _rollout.NormalizeAdvantages();

        int n = _rollout.Count;

        // The whole rollout is one batch — there is no minibatching, because with a single pass
        // over the data there is nothing to gain from splitting it.
        _rollout.Observations.AsSpan(0, n * _observationSize).CopyTo(_actor.InputBuffer(n));
        _rollout.Observations.AsSpan(0, n * _observationSize).CopyTo(_critic.InputBuffer(n));

        // --- Actor ---------------------------------------------------------------------------

        var logits = _actor.Forward(n);
        var actorGradient = _actor.OutputGradientBuffer(n);
        actorGradient.Clear();

        float policyLoss = 0f, entropyTotal = 0f;

        for (int i = 0; i < n; i++)
        {
            int action = (int)_rollout.Actions[i];
            float advantage = _rollout.Advantages[i];

            logits.Slice(i * _actionCount, _actionCount).CopyTo(_probabilities);
            SimdOps.SoftmaxInPlace(_probabilities);

            float logProbability = MathF.Log(MathF.Max(_probabilities[action], 1e-8f));
            policyLoss += -logProbability * advantage;

            // The plain policy gradient: no ratio, no clipping. The data came from exactly this
            // policy, so the importance weight is 1 by construction.
            CategoricalPolicy.AccumulateLogProbabilityGradient(
                actorGradient.Slice(i * _actionCount, _actionCount),
                _probabilities, action, -advantage / n);

            float entropy = CategoricalPolicy.Entropy(_probabilities);
            entropyTotal += entropy;

            CategoricalPolicy.AccumulateEntropyGradient(
                actorGradient.Slice(i * _actionCount, _actionCount),
                _probabilities, entropy, _options.EntropyCoefficient / n);
        }

        _actor.ZeroGradients();
        _actor.Backward(n);
        _actor.ApplyGradients(_actorOptimizer);

        // --- Critic --------------------------------------------------------------------------

        var values = _critic.Forward(n);
        var criticGradient = _critic.OutputGradientBuffer(n);

        float valueLoss = 0f;
        for (int i = 0; i < n; i++)
        {
            float error = values[i] - _rollout.Returns[i];
            valueLoss += 0.5f * error * error;
            criticGradient[i] = _options.ValueCoefficient * error / n;
        }

        _critic.ZeroGradients();
        _critic.Backward(n);
        _critic.ApplyGradients(_criticOptimizer);

        Metrics.PolicyLoss = policyLoss / n;
        Metrics.ValueLoss = valueLoss / n;
        Metrics.Entropy = entropyTotal / n;
        Metrics.UpdateCount++;

        _rollout.Clear();
    }

    /// <summary>Copies the actor's parameters out, for saving a trained policy.</summary>
    public float[] ExportParameters() => _actor.ExportParameters();

    /// <summary>Loads actor parameters.</summary>
    public void ImportParameters(ReadOnlySpan<float> parameters) => _actor.ImportParameters(parameters);
}
