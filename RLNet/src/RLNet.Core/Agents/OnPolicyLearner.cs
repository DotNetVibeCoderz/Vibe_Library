// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Buffers;
using RLNet.Neural;
using RLNet.Normalization;
using RLNet.Persistence;
using RLNet.Policies;
using RLNet.Utils;

namespace RLNet.Agents;

/// <summary>Which update an <see cref="OnPolicyLearner"/> runs after each rollout.</summary>
internal enum OnPolicyMethod
{
    /// <summary>Several epochs of clipped minibatch steps.</summary>
    Ppo,

    /// <summary>One plain policy-gradient step over the whole rollout.</summary>
    A2C,
}

/// <summary>Settings shared by PPO and A2C, mapped from their public option classes.</summary>
internal sealed record OnPolicySettings(
    OnPolicyMethod Method,
    int[] HiddenSizes,
    float LearningRate,
    float Gamma,
    float GaeLambda,
    int RolloutLength,
    int MinibatchSize,
    int Epochs,
    float ClipRange,
    float ValueCoefficient,
    float EntropyCoefficient,
    float MaxGradientNorm,
    bool NormalizeAdvantages,
    bool AnnealLearningRate,
    float TargetKl,
    bool NormalizeObservations,
    bool NormalizeRewards);

/// <summary>
/// The machinery PPO and A2C share: actor and critic, rollout collection across one or more
/// environments, normalisation, and both update rules — written once, generic over the
/// <see cref="PolicyHead"/> that makes an action discrete or continuous.
/// </summary>
/// <remarks>
/// <para>
/// Collection is batched. With <c>N</c> environments, one actor pass and one critic pass serve all
/// <c>N</c> of them, and the rollout holds <c>N</c> interleaved streams so advantages are computed
/// down each environment's own trajectory. A single environment is simply <c>N = 1</c>; the
/// arithmetic, and the order of every random draw, is then exactly what a dedicated
/// single-environment implementation would do.
/// </para>
/// <para>
/// The number of streams is taken from the first batch the learner sees and can change only
/// between rollouts — a rollout cannot be half from four environments and half from eight.
/// </para>
/// </remarks>
internal sealed class OnPolicyLearner : ICheckpointable
{
    private readonly OnPolicySettings _settings;
    private readonly PolicyHead _head;
    private readonly FastRandom _random;

    private readonly MlpNetwork _actor;
    private readonly MlpNetwork _critic;
    private readonly AdamOptimizer _actorOptimizer;
    private readonly AdamOptimizer _criticOptimizer;

    private readonly ObservationNormalizer? _observationNormalizer;
    private readonly RewardScaler? _rewardScaler;

    private readonly int _observationSize;
    private readonly float[] _observationScratch;
    private readonly int[] _order;
    private readonly float[] _minibatchAdvantages;

    private RolloutBuffer _rollout;

    // Carried from Act to Observe, one entry per stream, so each transition records the
    // log-probability and value of the observation the agent actually acted on — not a
    // re-evaluation after the network has moved.
    private float[] _pendingLogProbabilities;
    private float[] _pendingValues;
    private float[] _pendingActions;
    private float[] _lastValues;

    private float _progress;

    public OnPolicyLearner(OnPolicySettings settings, PolicyHead head, int observationSize, int? seed, IComputeBackend? backend)
    {
        _settings = settings;
        _head = head;
        _random = seed.HasValue ? new FastRandom(seed.Value) : new FastRandom();
        _observationSize = observationSize;

        int maxBatch = settings.Method == OnPolicyMethod.Ppo ? settings.MinibatchSize : settings.RolloutLength;

        // Tanh hidden layers rather than ReLU. The step size of both methods is bounded in
        // probability space, not in parameter space, and a bounded smooth activation keeps the
        // map from parameters to probabilities better conditioned — the same reason the
        // reference implementations use it for continuous control.
        _actor = new MlpNetwork(
            observationSize, settings.HiddenSizes, head.OutputSize,
            Activation.Tanh, Activation.Linear,
            maxBatch, _random,
            // A near-zero policy head starts the run at a near-uniform distribution (or a mean
            // near the middle of the action range). Starting from a sharp arbitrary policy costs
            // many rollouts of unlearning.
            outputScale: 0.01f,
            backend);

        _critic = new MlpNetwork(
            observationSize, settings.HiddenSizes, 1,
            Activation.Tanh, Activation.Linear,
            maxBatch, _random, 1f, backend);

        _actorOptimizer = new AdamOptimizer(_actor.ParameterCount, settings.LearningRate, maxGradientNorm: settings.MaxGradientNorm);
        _criticOptimizer = new AdamOptimizer(_critic.ParameterCount, settings.LearningRate, maxGradientNorm: settings.MaxGradientNorm);

        if (settings.NormalizeObservations) _observationNormalizer = new ObservationNormalizer(observationSize);
        if (settings.NormalizeRewards) _rewardScaler = new RewardScaler(settings.Gamma);

        _observationScratch = new float[observationSize];
        _order = new int[settings.RolloutLength];
        _minibatchAdvantages = new float[settings.MinibatchSize];

        _rollout = new RolloutBuffer(settings.RolloutLength, observationSize, head.ActionSize);
        _pendingLogProbabilities = new float[1];
        _pendingValues = new float[1];
        _pendingActions = new float[head.ActionSize];
        _lastValues = new float[1];
    }

    public AgentMetrics Metrics { get; } = new();

    public MlpNetwork Actor => _actor;

    /// <summary>Environments the current rollout is being collected from.</summary>
    public int Streams => _rollout.Streams;

    /// <summary>The unclipped action last sampled for <paramref name="stream"/>.</summary>
    public ReadOnlySpan<float> PendingAction(int stream) =>
        _pendingActions.AsSpan(stream * _head.ActionSize, _head.ActionSize);

    public void SetProgress(float progress) => _progress = Math.Clamp(progress, 0f, 1f);

    /// <summary>
    /// Chooses an action for each of <paramref name="count"/> observations, writing them into
    /// <paramref name="actions"/> on the head's scale — an index for discrete, the unclipped unit
    /// value for continuous.
    /// </summary>
    public void Act(ReadOnlySpan<float> observations, int count, Span<float> actions, bool deterministic)
    {
        if (observations.Length != count * _observationSize)
            throw new ArgumentException($"Expected {count} observations of {_observationSize} values.", nameof(observations));

        // Exploring selection is collection, and collection fixes the stream count. A greedy
        // evaluation in between — a single environment against an eight-stream rollout — must not
        // disturb it, so it neither reconfigures nor, when the shapes differ, records anything.
        if (!deterministic) Configure(count);
        bool record = count == Streams;

        int actionSize = _head.ActionSize;
        int outputSize = _head.OutputSize;

        for (int start = 0; start < count; start += _actor.MaxBatch)
        {
            int batch = Math.Min(_actor.MaxBatch, count - start);

            var actorInput = _actor.InputBuffer(batch);
            for (int i = 0; i < batch; i++)
            {
                var raw = observations.Slice((start + i) * _observationSize, _observationSize);
                if (_observationNormalizer is not null && !deterministic) _observationNormalizer.Update(raw);
                Prepare(raw, actorInput.Slice(i * _observationSize, _observationSize));
            }

            var outputs = _actor.Forward(batch);
            for (int i = 0; i < batch; i++)
            {
                int stream = start + i;
                var action = actions.Slice(stream * actionSize, actionSize);
                float logProbability = _head.Sample(
                    outputs.Slice(i * outputSize, outputSize), action, _random, deterministic, out float entropy);

                Metrics.Entropy = entropy;
                if (record)
                {
                    _pendingLogProbabilities[stream] = logProbability;
                    action.CopyTo(_pendingActions.AsSpan(stream * actionSize, actionSize));
                }
            }

            if (!record) continue;

            actorInput.CopyTo(_critic.InputBuffer(batch));
            var values = _critic.Forward(batch);
            for (int i = 0; i < batch; i++) _pendingValues[start + i] = values[i];
        }
    }

    /// <summary>Records one transition per stream, and runs an update when the rollout fills.</summary>
    /// <param name="actions">Per-stream actions on the head's scale, as stored in the rollout.</param>
    /// <param name="nextObservations">
    /// The state each transition led to, <em>before</em> any automatic reset — the state a
    /// truncated episode was cut off at, and the one a rollout ending mid-episode bootstraps from.
    /// </param>
    public void Observe(
        ReadOnlySpan<float> observations,
        ReadOnlySpan<float> actions,
        ReadOnlySpan<float> rewards,
        ReadOnlySpan<float> nextObservations,
        ReadOnlySpan<bool> terminated,
        ReadOnlySpan<bool> truncated)
    {
        int count = rewards.Length;
        if (count != Streams)
            throw new InvalidOperationException(
                $"Observed {count} transitions but the rollout is collecting from {Streams} environment(s). " +
                "Call the matching SelectAction first.");

        int actionSize = _head.ActionSize;

        for (int stream = 0; stream < count; stream++)
        {
            var next = nextObservations.Slice(stream * _observationSize, _observationSize);

            // A truncated episode needs the value of the state it was cut off at, so the return
            // can be bootstrapped through the cut. Evaluating it here — while the successor
            // observation is still in hand — is the only point at which it is available.
            float bootstrap = truncated[stream] ? Value(next) : 0f;

            float reward = _rewardScaler is null
                ? rewards[stream]
                : _rewardScaler.Scale(stream, rewards[stream], terminated[stream] || truncated[stream]);

            Prepare(observations.Slice(stream * _observationSize, _observationSize), _observationScratch);

            _rollout.Add(
                _observationScratch, actions.Slice(stream * actionSize, actionSize),
                _pendingLogProbabilities[stream], _pendingValues[stream],
                reward, terminated[stream], truncated[stream], bootstrap);
        }

        Metrics.StepCount += count;

        if (!_rollout.IsFull) return;

        // The rollout can end mid-episode. When it does, each stream's final return has to be
        // bootstrapped from where collection stopped rather than treated as an ending.
        for (int stream = 0; stream < count; stream++)
        {
            bool episodeContinues = !terminated[stream] && !truncated[stream];
            _lastValues[stream] = episodeContinues
                ? Value(nextObservations.Slice(stream * _observationSize, _observationSize))
                : 0f;
        }

        if (_settings.Method == OnPolicyMethod.Ppo) UpdatePpo();
        else UpdateA2C();

        // On-policy means exactly that: once the policy has moved, this data is no longer a
        // sample from it and cannot be reused.
        _rollout.Clear();
    }

    /// <summary>Critic estimate of one raw observation.</summary>
    private float Value(ReadOnlySpan<float> observation)
    {
        Prepare(observation, _critic.InputBuffer(1));
        return _critic.Forward(1)[0];
    }

    /// <summary>Copies an observation into a network input, standardising it if enabled.</summary>
    private void Prepare(ReadOnlySpan<float> observation, Span<float> destination)
    {
        if (_observationNormalizer is null) observation.CopyTo(destination);
        else _observationNormalizer.Normalize(observation, destination);
    }

    /// <summary>Sizes the rollout for <paramref name="streams"/> environments.</summary>
    private void Configure(int streams)
    {
        if (streams == Streams) return;

        if (_rollout.Count > 0)
            throw new InvalidOperationException(
                $"Cannot switch from {Streams} to {streams} environments in the middle of a rollout.");

        // The rollout length is the number of transitions per update, whatever their source, so
        // that the batch statistics — and the hyper-parameters tuned against them — stay the same
        // when environments are added. Each stream gets an equal share.
        int perStream = _settings.RolloutLength / streams;
        if (perStream < 1)
            throw new ArgumentException(
                $"{streams} environments exceed a rollout length of {_settings.RolloutLength}.", nameof(streams));

        _rollout = new RolloutBuffer(perStream * streams, _observationSize, _head.ActionSize, streams);
        _pendingLogProbabilities = new float[streams];
        _pendingValues = new float[streams];
        _pendingActions = new float[streams * _head.ActionSize];
        _lastValues = new float[streams];
        _rewardScaler?.Resize(streams);
    }

    // --- PPO ---------------------------------------------------------------------------------

    private void UpdatePpo()
    {
        if (_settings.AnnealLearningRate)
        {
            // Late in a run the policy is close to its final form and a full-size step is more
            // likely to undo progress than to add to it.
            float rate = _settings.LearningRate * (1f - _progress);
            _actorOptimizer.LearningRate = rate;
            _criticOptimizer.LearningRate = rate;
        }

        _rollout.ComputeAdvantages(_lastValues, _settings.Gamma, _settings.GaeLambda);

        int total = _rollout.Count;
        int minibatch = Math.Min(_settings.MinibatchSize, total);

        float policyLossSum = 0f, valueLossSum = 0f, entropySum = 0f;
        int gradientSteps = 0;
        bool stopEarly = false;

        for (int epoch = 0; epoch < _settings.Epochs && !stopEarly; epoch++)
        {
            _rollout.ShuffleIndices(_order, _random);

            for (int start = 0; start + minibatch <= total; start += minibatch)
            {
                float kl = UpdatePpoMinibatch(
                    _order.AsSpan(start, minibatch), out float policyLoss, out float valueLoss, out float entropy);

                policyLossSum += policyLoss;
                valueLossSum += valueLoss;
                entropySum += entropy;
                gradientSteps++;

                // The clip bounds each action's ratio but not the distribution as a whole; a
                // rollout can drift far in KL while every individual ratio stays inside the
                // range. Stopping the update is cheaper than recovering from the collapse.
                if (_settings.TargetKl > 0f && kl > _settings.TargetKl * 1.5f)
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
    }

    /// <summary>Runs one gradient step over a minibatch and returns its mean KL from the collecting policy.</summary>
    private float UpdatePpoMinibatch(ReadOnlySpan<int> indices, out float policyLoss, out float valueLoss, out float entropy)
    {
        int n = indices.Length;
        int actionSize = _head.ActionSize;
        int outputSize = _head.OutputSize;

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

        if (_settings.NormalizeAdvantages) Standardize(_minibatchAdvantages.AsSpan(0, n));

        // --- Actor ---------------------------------------------------------------------------

        var outputs = _actor.Forward(n);
        var actorGradient = _actor.OutputGradientBuffer(n);
        actorGradient.Clear();
        _head.BeginUpdate();

        policyLoss = 0f;
        entropy = 0f;
        float klSum = 0f;

        for (int i = 0; i < n; i++)
        {
            int index = indices[i];
            var action = _rollout.Actions.AsSpan(index * actionSize, actionSize);
            var output = outputs.Slice(i * outputSize, outputSize);
            var gradient = actorGradient.Slice(i * outputSize, outputSize);
            float oldLogProbability = _rollout.LogProbabilities[index];
            float advantage = _minibatchAdvantages[i];

            float logProbability = _head.Evaluate(output, action, out float sampleEntropy);
            float ratio = MathF.Exp(logProbability - oldLogProbability);

            float unclipped = ratio * advantage;
            float clipped = Math.Clamp(ratio, 1f - _settings.ClipRange, 1f + _settings.ClipRange) * advantage;

            // The objective is the minimum of the two, so it is a pessimistic bound on the
            // improvement. Maximising a lower bound is what makes the step safe.
            policyLoss += -MathF.Min(unclipped, clipped);

            // Where the clipped branch is the smaller one, the ratio is outside the trust region
            // and the clip has flattened the objective — so the gradient there is exactly zero.
            // That flat region is the entire mechanism; without it PPO is vanilla policy gradient
            // run several times on the same data, which diverges.
            if (unclipped <= clipped)
                _head.AccumulateLogProbabilityGradient(gradient, output, action, -(advantage * ratio / n));

            entropy += sampleEntropy;
            _head.AccumulateEntropyGradient(gradient, sampleEntropy, _settings.EntropyCoefficient / n);

            // Schulman's low-variance KL estimator. The naive (old - new) estimate is unbiased
            // but noisy enough around zero to trip the early stop at random.
            float logRatio = logProbability - oldLogProbability;
            klSum += MathF.Exp(logRatio) - 1f - logRatio;
        }

        _actor.ZeroGradients();
        _actor.Backward(n);
        _actor.ApplyGradients(_actorOptimizer);
        _head.ApplyUpdate(_actorOptimizer.LearningRate);

        valueLoss = UpdateCritic(n, indices);

        policyLoss /= n;
        entropy /= n;
        return klSum / n;
    }

    // --- A2C ---------------------------------------------------------------------------------

    private void UpdateA2C()
    {
        _rollout.ComputeAdvantages(_lastValues, _settings.Gamma, _settings.GaeLambda);
        if (_settings.NormalizeAdvantages) _rollout.NormalizeAdvantages();

        int n = _rollout.Count;
        int actionSize = _head.ActionSize;
        int outputSize = _head.OutputSize;

        // The whole rollout is one batch — there is no minibatching, because with a single pass
        // over the data there is nothing to gain from splitting it.
        _rollout.Observations.AsSpan(0, n * _observationSize).CopyTo(_actor.InputBuffer(n));
        _rollout.Observations.AsSpan(0, n * _observationSize).CopyTo(_critic.InputBuffer(n));

        var outputs = _actor.Forward(n);
        var actorGradient = _actor.OutputGradientBuffer(n);
        actorGradient.Clear();
        _head.BeginUpdate();

        float policyLoss = 0f, entropyTotal = 0f;

        for (int i = 0; i < n; i++)
        {
            var action = _rollout.Actions.AsSpan(i * actionSize, actionSize);
            var output = outputs.Slice(i * outputSize, outputSize);
            var gradient = actorGradient.Slice(i * outputSize, outputSize);
            float advantage = _rollout.Advantages[i];

            float logProbability = _head.Evaluate(output, action, out float entropy);
            policyLoss += -logProbability * advantage;

            // The plain policy gradient: no ratio, no clipping. The data came from exactly this
            // policy, so the importance weight is 1 by construction.
            _head.AccumulateLogProbabilityGradient(gradient, output, action, -advantage / n);

            entropyTotal += entropy;
            _head.AccumulateEntropyGradient(gradient, entropy, _settings.EntropyCoefficient / n);
        }

        _actor.ZeroGradients();
        _actor.Backward(n);
        _actor.ApplyGradients(_actorOptimizer);
        _head.ApplyUpdate(_actorOptimizer.LearningRate);

        float valueLoss = UpdateCritic(n, indices: default);

        Metrics.PolicyLoss = policyLoss / n;
        Metrics.ValueLoss = valueLoss;
        Metrics.Entropy = entropyTotal / n;
        Metrics.UpdateCount++;
    }

    // --- Shared ------------------------------------------------------------------------------

    /// <summary>
    /// One critic step toward the lambda-returns, over the batch already in the critic's input
    /// buffer. <paramref name="indices"/> maps batch rows to rollout slots; empty means identity.
    /// </summary>
    private float UpdateCritic(int n, ReadOnlySpan<int> indices)
    {
        var values = _critic.Forward(n);
        var criticGradient = _critic.OutputGradientBuffer(n);

        float valueLoss = 0f;
        for (int i = 0; i < n; i++)
        {
            float error = values[i] - _rollout.Returns[indices.IsEmpty ? i : indices[i]];
            valueLoss += 0.5f * error * error;

            // d/dV of ½(V - R)² is (V - R). The coefficient rescales the value loss against the
            // policy loss; they share no parameters here but do share a learning rate.
            criticGradient[i] = _settings.ValueCoefficient * error / n;
        }

        _critic.ZeroGradients();
        _critic.Backward(n);
        _critic.ApplyGradients(_criticOptimizer);

        return valueLoss / n;
    }

    private static void Standardize(Span<float> values)
    {
        int n = values.Length;

        float mean = 0f;
        for (int i = 0; i < n; i++) mean += values[i];
        mean /= n;

        float variance = 0f;
        for (int i = 0; i < n; i++)
        {
            float d = values[i] - mean;
            variance += d * d;
        }

        float invStd = 1f / (MathF.Sqrt(variance / n) + 1e-8f);
        for (int i = 0; i < n; i++)
            values[i] = (values[i] - mean) * invStd;
    }

    // --- Checkpoint ----------------------------------------------------------------------------

    public void WriteState(CheckpointWriter writer)
    {
        writer.Write("actor", _actor);
        writer.Write("critic", _critic);
        writer.Write("actorOptimizer", _actorOptimizer);
        writer.Write("criticOptimizer", _criticOptimizer);
        writer.WriteComponent("head", _head);
        writer.Write("random", _random);
        writer.Write("progress", _progress);
        writer.Write("steps", Metrics.StepCount);
        writer.Write("updates", Metrics.UpdateCount);
        if (_observationNormalizer is not null) writer.WriteComponent("observationNormalizer", _observationNormalizer);
        if (_rewardScaler is not null) writer.WriteComponent("rewardScaler", _rewardScaler);
    }

    public void ReadState(CheckpointReader reader)
    {
        reader.Read("actor", _actor);
        reader.Read("critic", _critic);
        reader.Read("actorOptimizer", _actorOptimizer);
        reader.Read("criticOptimizer", _criticOptimizer);
        reader.ReadComponent("head", _head);
        reader.Read("random", _random);
        _progress = reader.ReadSingle("progress");
        Metrics.StepCount = reader.ReadInt64("steps");
        Metrics.UpdateCount = reader.ReadInt64("updates");
        if (_observationNormalizer is not null) reader.ReadComponent("observationNormalizer", _observationNormalizer);
        if (_rewardScaler is not null) reader.ReadComponent("rewardScaler", _rewardScaler);

        // A half-collected rollout belongs to the policy that was running before the load.
        _rollout.Clear();
    }
}
