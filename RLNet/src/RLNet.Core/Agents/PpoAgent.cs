// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Buffers;
using RLNet.Neural;
using RLNet.Policies;
using RLNet.Spaces;
using RLNet.Utils;

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
/// </remarks>
public sealed class PpoAgent : IDiscreteAgent
{
    private readonly PpoOptions _options;
    private readonly RolloutBuffer _rollout;
    private readonly FastRandom _random;

    private readonly MlpNetwork _actor;
    private readonly MlpNetwork _critic;
    private readonly AdamOptimizer _actorOptimizer;
    private readonly AdamOptimizer _criticOptimizer;

    private readonly int _actionCount;
    private readonly int _observationSize;

    private readonly float[] _probabilities;
    private readonly int[] _order;
    private readonly float[] _minibatchAdvantages;

    // Carried between SelectAction and Observe so the value estimate is the one computed from
    // the observation the agent actually acted on, not a re-evaluation after the network moved.
    private float _pendingLogProbability;
    private float _pendingValue;

    private float _progress;

    public PpoAgent(
        Space observationSpace,
        DiscreteSpace actionSpace,
        PpoOptions? options = null,
        int? seed = null,
        IComputeBackend? backend = null)
    {
        _options = options ?? new PpoOptions();
        _random = seed.HasValue ? new FastRandom(seed.Value) : new FastRandom();

        _observationSize = observationSpace.FlatSize;
        _actionCount = actionSpace.Count;

        _rollout = new RolloutBuffer(_options.RolloutLength, _observationSize, 1);

        // Tanh hidden layers rather than ReLU. PPO's step size is bounded in probability space,
        // not in parameter space, and a bounded smooth activation keeps the map from parameters
        // to probabilities better conditioned — the same reason the reference implementations
        // use it for continuous control.
        _actor = new MlpNetwork(
            _observationSize, _options.HiddenSizes, _actionCount,
            Activation.Tanh, Activation.Linear,
            _options.MinibatchSize, _random,
            // A near-zero policy head starts the run at a near-uniform distribution. Starting
            // from a sharp arbitrary policy costs many rollouts of unlearning.
            outputScale: 0.01f,
            backend);

        _critic = new MlpNetwork(
            _observationSize, _options.HiddenSizes, 1,
            Activation.Tanh, Activation.Linear,
            _options.MinibatchSize, _random, 1f, backend);

        _actorOptimizer = new AdamOptimizer(
            _actor.ParameterCount, _options.LearningRate, maxGradientNorm: _options.MaxGradientNorm);
        _criticOptimizer = new AdamOptimizer(
            _critic.ParameterCount, _options.LearningRate, maxGradientNorm: _options.MaxGradientNorm);

        _probabilities = new float[_actionCount];
        _order = new int[_options.RolloutLength];
        _minibatchAdvantages = new float[_options.MinibatchSize];
    }

    public string Name => "PPO";
    public AgentMetrics Metrics { get; } = new();

    public void SetProgress(float progress) => _progress = Math.Clamp(progress, 0f, 1f);

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
        // A truncated episode needs the value of the state it was cut off at, so the return can
        // be bootstrapped through the cut. Evaluating it here — while the successor observation
        // is still in hand — is the only point at which it is available.
        float bootstrap = truncated ? _critic.Forward(nextObservation)[0] : 0f;

        _rollout.AddDiscrete(
            observation, action, _pendingLogProbability, _pendingValue,
            reward, terminated, truncated, bootstrap);

        Metrics.StepCount++;

        if (_rollout.IsFull)
        {
            // The rollout can end mid-episode. When it does, the final step's return has to be
            // bootstrapped from where collection stopped rather than treated as an ending.
            bool episodeContinues = !terminated && !truncated;
            float lastValue = episodeContinues ? _critic.Forward(nextObservation)[0] : 0f;
            Update(lastValue);
        }
    }

    public void OnEpisodeEnd() { }

    private void Update(float lastValue)
    {
        if (_options.AnnealLearningRate)
        {
            // Late in a run the policy is close to its final form and a full-size step is more
            // likely to undo progress than to add to it.
            float rate = _options.LearningRate * (1f - _progress);
            _actorOptimizer.LearningRate = rate;
            _criticOptimizer.LearningRate = rate;
        }

        _rollout.ComputeAdvantages(lastValue, _options.Gamma, _options.GaeLambda);

        int total = _rollout.Count;
        int minibatch = Math.Min(_options.MinibatchSize, total);

        float policyLossSum = 0f, valueLossSum = 0f, entropySum = 0f, klSum = 0f;
        int gradientSteps = 0;
        bool stopEarly = false;

        for (int epoch = 0; epoch < _options.Epochs && !stopEarly; epoch++)
        {
            _rollout.ShuffleIndices(_order, _random);

            for (int start = 0; start + minibatch <= total; start += minibatch)
            {
                var indices = _order.AsSpan(start, minibatch);

                float kl = UpdateMinibatch(
                    indices, out float policyLoss, out float valueLoss, out float entropy);

                policyLossSum += policyLoss;
                valueLossSum += valueLoss;
                entropySum += entropy;
                klSum += kl;
                gradientSteps++;

                // The clip bounds each action's ratio but not the distribution as a whole; a
                // rollout can drift far in KL while every individual ratio stays inside the
                // range. Stopping the update is cheaper than recovering from the collapse.
                if (_options.TargetKl > 0f && kl > _options.TargetKl * 1.5f)
                {
                    stopEarly = true;
                    break;
                }
            }
        }

        if (gradientSteps > 0)
        {
            Metrics.PolicyLoss = policyLossSum / gradientSteps;
            Metrics.ValueLoss = valueLossSum / gradientSteps;
            Metrics.Entropy = entropySum / gradientSteps;
            Metrics.UpdateCount += gradientSteps;
        }

        // On-policy means exactly that: once the policy has moved, this data is no longer a
        // sample from it and cannot be reused.
        _rollout.Clear();
    }

    /// <summary>Runs one gradient step over a minibatch and returns its mean KL from the collecting policy.</summary>
    private float UpdateMinibatch(
        ReadOnlySpan<int> indices,
        out float policyLoss,
        out float valueLoss,
        out float entropy)
    {
        int n = indices.Length;

        // Gather the minibatch straight into the networks' own input buffers.
        var actorInput = _actor.InputBuffer(n);
        var criticInput = _critic.InputBuffer(n);
        for (int i = 0; i < n; i++)
        {
            var observation = _rollout.Observations.AsSpan(indices[i] * _observationSize, _observationSize);
            observation.CopyTo(actorInput.Slice(i * _observationSize, _observationSize));
            observation.CopyTo(criticInput.Slice(i * _observationSize, _observationSize));

            _minibatchAdvantages[i] = _rollout.Advantages[indices[i]];
        }

        if (_options.NormalizeAdvantages) NormalizeMinibatchAdvantages(n);

        // --- Actor ---------------------------------------------------------------------------

        var logits = _actor.Forward(n);
        var actorGradient = _actor.OutputGradientBuffer(n);
        actorGradient.Clear();

        policyLoss = 0f;
        entropy = 0f;
        float klSum = 0f;

        for (int i = 0; i < n; i++)
        {
            int index = indices[i];
            int action = (int)_rollout.Actions[index];
            float oldLogProbability = _rollout.LogProbabilities[index];
            float advantage = _minibatchAdvantages[i];

            logits.Slice(i * _actionCount, _actionCount).CopyTo(_probabilities);
            SimdOps.SoftmaxInPlace(_probabilities);

            float logProbability = MathF.Log(MathF.Max(_probabilities[action], 1e-8f));
            float ratio = MathF.Exp(logProbability - oldLogProbability);

            float unclipped = ratio * advantage;
            float clipped = Math.Clamp(ratio, 1f - _options.ClipRange, 1f + _options.ClipRange) * advantage;

            // The objective is the minimum of the two, so it is a pessimistic bound on the
            // improvement. Maximising a lower bound is what makes the step safe.
            policyLoss += -MathF.Min(unclipped, clipped);

            // Where the clipped branch is the smaller one, the ratio is outside the trust region
            // and the clip has flattened the objective — so the gradient there is exactly zero.
            // That flat region is the entire mechanism; without it PPO is vanilla policy gradient
            // run several times on the same data, which diverges.
            if (unclipped <= clipped)
            {
                float coefficient = advantage * ratio / n;
                CategoricalPolicy.AccumulateLogProbabilityGradient(
                    actorGradient.Slice(i * _actionCount, _actionCount),
                    _probabilities, action, -coefficient);
            }

            float sampleEntropy = CategoricalPolicy.Entropy(_probabilities);
            entropy += sampleEntropy;

            CategoricalPolicy.AccumulateEntropyGradient(
                actorGradient.Slice(i * _actionCount, _actionCount),
                _probabilities, sampleEntropy, _options.EntropyCoefficient / n);

            // Schulman's low-variance KL estimator. The naive (old - new) estimate is unbiased
            // but noisy enough around zero to trip the early stop at random.
            float logRatio = logProbability - oldLogProbability;
            klSum += MathF.Exp(logRatio) - 1f - logRatio;
        }

        _actor.ZeroGradients();
        _actor.Backward(n);
        _actor.ApplyGradients(_actorOptimizer);

        // --- Critic --------------------------------------------------------------------------

        var values = _critic.Forward(n);
        var criticGradient = _critic.OutputGradientBuffer(n);

        valueLoss = 0f;
        for (int i = 0; i < n; i++)
        {
            float error = values[i] - _rollout.Returns[indices[i]];
            valueLoss += 0.5f * error * error;

            // d/dV of ½(V - R)² is (V - R). The coefficient rescales the value loss against the
            // policy loss; they share no parameters here but do share a learning rate.
            criticGradient[i] = _options.ValueCoefficient * error / n;
        }

        _critic.ZeroGradients();
        _critic.Backward(n);
        _critic.ApplyGradients(_criticOptimizer);

        policyLoss /= n;
        valueLoss /= n;
        entropy /= n;
        return klSum / n;
    }

    private void NormalizeMinibatchAdvantages(int n)
    {
        var advantages = _minibatchAdvantages.AsSpan(0, n);

        float mean = 0f;
        for (int i = 0; i < n; i++) mean += advantages[i];
        mean /= n;

        float variance = 0f;
        for (int i = 0; i < n; i++)
        {
            float d = advantages[i] - mean;
            variance += d * d;
        }

        float invStd = 1f / (MathF.Sqrt(variance / n) + 1e-8f);
        for (int i = 0; i < n; i++)
            advantages[i] = (advantages[i] - mean) * invStd;
    }

    /// <summary>Copies the actor's parameters out, for saving a trained policy.</summary>
    public float[] ExportParameters() => _actor.ExportParameters();

    /// <summary>Loads actor parameters.</summary>
    public void ImportParameters(ReadOnlySpan<float> parameters) => _actor.ImportParameters(parameters);
}
