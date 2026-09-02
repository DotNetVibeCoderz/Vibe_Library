// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Buffers;
using RLNet.Neural;
using RLNet.Spaces;
using RLNet.Utils;

namespace RLNet.Agents;

/// <summary>Tunable settings for <see cref="SacAgent"/>.</summary>
public sealed class SacOptions
{
    /// <summary>Hidden layer widths, used for the actor and both critics.</summary>
    public int[] HiddenSizes { get; set; } = [256, 256];

    /// <summary>Adam step size, shared by actor, critics and temperature.</summary>
    public float LearningRate { get; set; } = 3e-4f;

    /// <summary>Discount factor.</summary>
    public float Gamma { get; set; } = 0.99f;

    /// <summary>Polyak coefficient for the target critics. Small means slow and stable.</summary>
    public float Tau { get; set; } = 0.005f;

    /// <summary>Transitions per gradient step.</summary>
    public int BatchSize { get; set; } = 256;

    /// <summary>Transitions collected — by a uniformly random policy — before learning begins.</summary>
    public int LearningStarts { get; set; } = 1_000;

    /// <summary>Environment steps between gradient steps.</summary>
    public int TrainFrequency { get; set; } = 1;

    /// <summary>Replay capacity used when the agent builds its own buffer.</summary>
    public int BufferCapacity { get; set; } = 200_000;

    /// <summary>
    /// Learn the entropy temperature instead of holding it fixed.
    /// </summary>
    /// <remarks>
    /// The single most useful thing about SAC in practice. The right trade-off between reward and
    /// exploration is not a constant — it differs per environment and changes over a run — and
    /// tuning it by hand is most of the work of using the algorithm. Learning it against a target
    /// entropy removes the library's most annoying hyper-parameter.
    /// </remarks>
    public bool AutoTuneTemperature { get; set; } = true;

    /// <summary>Fixed temperature, used when <see cref="AutoTuneTemperature"/> is off.</summary>
    public float Temperature { get; set; } = 0.2f;

    /// <summary>
    /// Entropy the policy is steered toward. Defaults to the standard heuristic
    /// <c>-actionDimensions</c> when left at 0.
    /// </summary>
    public float TargetEntropy { get; set; }

    /// <summary>Global gradient-norm clip, or 0 to disable.</summary>
    public float MaxGradientNorm { get; set; } = 10f;
}

/// <summary>
/// Soft Actor-Critic: off-policy continuous control with a stochastic policy and an entropy bonus.
/// </summary>
/// <remarks>
/// <para>
/// Haarnoja et al. (2018). The objective is not just return but return plus policy entropy, so
/// the agent is rewarded for staying uncertain wherever uncertainty is cheap. That turns
/// exploration from something bolted on — injected noise, decayed by hand — into part of what is
/// being optimised, which is why SAC needs so much less tuning than the alternatives.
/// </para>
/// <para>
/// The policy is a Gaussian squashed through a <c>tanh</c> so actions land in <c>[-1, 1]</c>.
/// That squashing changes the density, and the correction — the <c>log(1 - tanh²)</c> term in
/// <see cref="SampleWithLogProbability"/> — is not optional bookkeeping: without it the reported
/// log-probability is wrong, the temperature is tuned against a fiction, and the policy quietly
/// saturates at the action bounds.
/// </para>
/// <para>
/// Actions are stored and learned on the unit scale. Rescaling to the environment's bounds
/// happens only at the edge, in <see cref="SelectAction"/>, so nothing inside the algorithm has
/// to differentiate through it.
/// </para>
/// </remarks>
public sealed class SacAgent : IContinuousAgent
{
    private const float LogStdMin = -20f;
    private const float LogStdMax = 2f;

    private readonly SacOptions _options;
    private readonly BoxSpace _actionSpace;
    private readonly IReplayBuffer _buffer;
    private readonly ReplayBatch _batch;
    private readonly FastRandom _random;

    private readonly MlpNetwork _actor;          // emits [means, logStds]
    private readonly AdamOptimizer _actorOptimizer;
    private readonly QNetworkPair _critics;

    private readonly int _observationSize;
    private readonly int _actionSize;

    // Temperature is learned in log space: it must stay positive, and an unconstrained parameter
    // through exp() is the standard way to guarantee that without a projection step.
    private float _logTemperature;
    private float _logTemperatureMoment1;
    private float _logTemperatureMoment2;
    private int _temperatureStep;

    private readonly float[] _sampledActions;    // unit scale, [batch, actionSize]
    private readonly float[] _logProbabilities;
    private readonly float[] _noise;             // reparameterisation draws, kept for the backward pass
    private readonly float[] _targets;
    private readonly float[] _tdErrors;
    private readonly float[] _scaledAction;      // single-action scratch for SelectAction

    private int _stepsSinceTrain;

    public SacAgent(
        Space observationSpace,
        BoxSpace actionSpace,
        SacOptions? options = null,
        IReplayBuffer? buffer = null,
        int? seed = null,
        IComputeBackend? backend = null)
    {
        _options = options ?? new SacOptions();
        _actionSpace = actionSpace;
        _random = seed.HasValue ? new FastRandom(seed.Value) : new FastRandom();

        _observationSize = observationSpace.FlatSize;
        _actionSize = actionSpace.FlatSize;

        _buffer = buffer ?? new UniformReplayBuffer(_options.BufferCapacity, _observationSize, _actionSize);
        _batch = new ReplayBatch(_options.BatchSize, _observationSize, _actionSize);

        _actor = new MlpNetwork(
            _observationSize, _options.HiddenSizes, _actionSize * 2,
            Activation.ReLU, Activation.Linear,
            _options.BatchSize, _random, 1f, backend);

        _actorOptimizer = new AdamOptimizer(
            _actor.ParameterCount, _options.LearningRate, maxGradientNorm: _options.MaxGradientNorm);

        _critics = new QNetworkPair(
            _observationSize, _actionSize, _options.HiddenSizes,
            _options.LearningRate, _options.BatchSize, _random,
            _options.MaxGradientNorm, backend);

        _logTemperature = MathF.Log(_options.Temperature);

        _sampledActions = new float[_options.BatchSize * _actionSize];
        _logProbabilities = new float[_options.BatchSize];
        _noise = new float[_options.BatchSize * _actionSize];
        _targets = new float[_options.BatchSize];
        _tdErrors = new float[_options.BatchSize];
        _scaledAction = new float[_actionSize];

        TargetEntropy = _options.TargetEntropy != 0f ? _options.TargetEntropy : -_actionSize;
    }

    public string Name => "SAC";
    public AgentMetrics Metrics { get; } = new();

    /// <summary>Entropy the temperature is tuned to hold the policy at.</summary>
    public float TargetEntropy { get; }

    /// <summary>Current entropy temperature.</summary>
    public float Temperature => _options.AutoTuneTemperature ? MathF.Exp(_logTemperature) : _options.Temperature;

    /// <summary>The replay buffer in use.</summary>
    public IReplayBuffer Buffer => _buffer;

    /// <summary>SAC runs at a constant learning rate; progress is not used.</summary>
    public void SetProgress(float progress) { }

    public void SelectAction(ReadOnlySpan<float> observation, Span<float> action, bool deterministic = false)
    {
        // Before learning starts, act uniformly at random. An untrained actor's output is not
        // random so much as arbitrary — it concentrates wherever its initial weights point — and
        // a replay buffer seeded from it covers the action space badly.
        if (!deterministic && Metrics.StepCount < _options.LearningStarts)
        {
            for (int i = 0; i < _actionSize; i++) _scaledAction[i] = _random.NextRange(-1f, 1f);
        }
        else
        {
            var output = _actor.Forward(observation);

            for (int i = 0; i < _actionSize; i++)
            {
                float mean = output[i];

                if (deterministic)
                {
                    // The tanh of the mean, not the mean of the tanh: evaluation wants the mode
                    // of the squashed distribution, which is where the mean maps to.
                    _scaledAction[i] = MathF.Tanh(mean);
                }
                else
                {
                    float logStd = Math.Clamp(output[_actionSize + i], LogStdMin, LogStdMax);
                    _scaledAction[i] = MathF.Tanh(mean + MathF.Exp(logStd) * _random.NextGaussian());
                }
            }
        }

        _scaledAction.AsSpan(0, _actionSize).CopyTo(action);
        _actionSpace.ScaleFromUnit(action);
    }

    public void Observe(
        ReadOnlySpan<float> observation,
        ReadOnlySpan<float> action,
        float reward,
        ReadOnlySpan<float> nextObservation,
        bool terminated,
        bool truncated)
    {
        // Store on the unit scale the critics and actor work in, undoing the rescaling applied
        // on the way out.
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

    public void OnEpisodeEnd() { }

    private void Train()
    {
        _buffer.Sample(_options.BatchSize, _batch, _random);
        int batch = _batch.Count;

        // --- Critics -------------------------------------------------------------------------
        // The soft Bellman target: the usual bootstrap, minus the temperature-weighted
        // log-probability of the successor action. That subtraction is the entropy bonus — a
        // successor state the policy is confident about is worth less than one it can act freely
        // in, which is what makes the agent prefer to keep its options open.

        SampleWithLogProbability(_batch.NextObservations, batch, storeNoise: false);
        _critics.EvaluateTargetMinimum(_batch.NextObservations, _sampledActions, batch, _targets.AsSpan(0, batch));

        float temperature = Temperature;
        for (int i = 0; i < batch; i++)
        {
            float softValue = _targets[i] - temperature * _logProbabilities[i];
            _targets[i] = _batch.Terminated[i]
                ? _batch.Rewards[i]
                : _batch.Rewards[i] + _options.Gamma * softValue;
        }

        float criticLoss = _critics.Update(
            _batch.Observations, _batch.Actions, _targets.AsSpan(0, batch), batch,
            _batch.Weights.AsSpan(0, batch), _tdErrors.AsSpan(0, batch));

        _buffer.UpdatePriorities(_batch.Indices.AsSpan(0, batch), _tdErrors.AsSpan(0, batch));

        // --- Actor ---------------------------------------------------------------------------
        // Fresh actions are sampled from the current policy — not replayed. The actor is being
        // asked "what would you do here now", and the critic scores that.

        var actorOutput = SampleWithLogProbability(_batch.Observations, batch, storeNoise: true);
        var actionGradient = _critics.ActionGradientOfMinimum(_batch.Observations, _sampledActions, batch);

        var gradient = _actor.OutputGradientBuffer(batch);
        gradient.Clear();

        float actorLoss = 0f, entropyTotal = 0f, logProbabilityTotal = 0f;

        for (int i = 0; i < batch; i++)
        {
            logProbabilityTotal += _logProbabilities[i];
            entropyTotal -= _logProbabilities[i];

            for (int j = 0; j < _actionSize; j++)
            {
                int flat = i * _actionSize + j;

                float action = _sampledActions[flat];
                float logStd = Math.Clamp(actorOutput[i * _actionSize * 2 + _actionSize + j], LogStdMin, LogStdMax);
                float std = MathF.Exp(logStd);
                float epsilon = _noise[flat];

                // The squashing derivative, which every term below flows through.
                float dActionDPreTanh = 1f - action * action;

                // d(logProbability)/d(mean) and /d(logStd), for a tanh-squashed Gaussian under
                // the reparameterisation. The Gaussian's own density contributes nothing to the
                // mean derivative — the standardised noise is held fixed — so what survives comes
                // entirely from the tanh correction term.
                float dLogProbabilityDMean = 2f * action;
                float dLogProbabilityDLogStd = -1f + 2f * action * std * epsilon;

                // The actor minimises  α·logπ − Q, so it climbs Q while being pulled toward
                // higher entropy in proportion to the temperature.
                float dQ = actionGradient[flat];

                gradient[i * _actionSize * 2 + j] =
                    (temperature * dLogProbabilityDMean - dQ * dActionDPreTanh) / batch;

                gradient[i * _actionSize * 2 + _actionSize + j] =
                    (temperature * dLogProbabilityDLogStd - dQ * dActionDPreTanh * std * epsilon) / batch;
            }
        }

        actorLoss = temperature * logProbabilityTotal / batch;

        _actor.ZeroGradients();
        _actor.Backward(batch);
        _actor.ApplyGradients(_actorOptimizer);

        // --- Temperature ---------------------------------------------------------------------

        if (_options.AutoTuneTemperature)
            UpdateTemperature(logProbabilityTotal / batch);

        _critics.SoftUpdateTargets(_options.Tau);

        Metrics.ValueLoss = criticLoss;
        Metrics.PolicyLoss = actorLoss;
        Metrics.Entropy = entropyTotal / batch;
        Metrics.Temperature = Temperature;
        Metrics.UpdateCount++;
    }

    /// <summary>
    /// Samples one action per observation from the current policy and records its
    /// log-probability, writing into <see cref="_sampledActions"/> and
    /// <see cref="_logProbabilities"/>.
    /// </summary>
    /// <param name="storeNoise">
    /// Keep the standardised draws. The actor's backward pass needs the exact noise that produced
    /// each action — that is what "reparameterisation" means, and re-drawing would make the
    /// gradient describe a different sample than the one the critic scored.
    /// </param>
    private ReadOnlySpan<float> SampleWithLogProbability(ReadOnlySpan<float> observations, int batch, bool storeNoise)
    {
        observations[..(batch * _observationSize)].CopyTo(_actor.InputBuffer(batch));
        var output = _actor.Forward(batch);

        for (int i = 0; i < batch; i++)
        {
            float logProbability = 0f;

            for (int j = 0; j < _actionSize; j++)
            {
                float mean = output[i * _actionSize * 2 + j];
                float logStd = Math.Clamp(output[i * _actionSize * 2 + _actionSize + j], LogStdMin, LogStdMax);
                float std = MathF.Exp(logStd);

                float epsilon = _random.NextGaussian();
                float preTanh = mean + std * epsilon;
                float action = MathF.Tanh(preTanh);

                int flat = i * _actionSize + j;
                _sampledActions[flat] = action;
                if (storeNoise) _noise[flat] = epsilon;

                // Gaussian log-density at the pre-squash value, then the change-of-variables
                // correction for the tanh. The epsilon inside the log guards the case where a
                // saturated action rounds |a| to exactly 1 and the correction would diverge.
                logProbability += -0.5f * epsilon * epsilon - logStd - 0.9189385f;
                logProbability -= MathF.Log(MathF.Max(1f - action * action, 1e-6f));
            }

            _logProbabilities[i] = logProbability;
        }

        return output;
    }

    /// <summary>
    /// Nudges the temperature so the policy's entropy tracks <see cref="TargetEntropy"/>.
    /// </summary>
    /// <remarks>
    /// The loss is <c>-logα · (logπ + targetEntropy)</c>. When the policy is more certain than
    /// the target — log-probability above it — the temperature rises and the entropy bonus grows,
    /// pushing the policy back toward exploring. It is a one-parameter controller, so it carries
    /// its own two Adam moments here rather than justifying a whole optimiser.
    /// </remarks>
    private void UpdateTemperature(float meanLogProbability)
    {
        float gradient = -(meanLogProbability + TargetEntropy);

        _temperatureStep++;
        const float beta1 = 0.9f, beta2 = 0.999f, epsilon = 1e-8f;

        _logTemperatureMoment1 = beta1 * _logTemperatureMoment1 + (1f - beta1) * gradient;
        _logTemperatureMoment2 = beta2 * _logTemperatureMoment2 + (1f - beta2) * gradient * gradient;

        float corrected1 = _logTemperatureMoment1 / (1f - MathF.Pow(beta1, _temperatureStep));
        float corrected2 = _logTemperatureMoment2 / (1f - MathF.Pow(beta2, _temperatureStep));

        _logTemperature -= _options.LearningRate * corrected1 / (MathF.Sqrt(corrected2) + epsilon);
        _logTemperature = Math.Clamp(_logTemperature, -10f, 2f);
    }

    /// <summary>Copies the actor's parameters out, for saving a trained policy.</summary>
    public float[] ExportParameters() => _actor.ExportParameters();

    /// <summary>Loads actor parameters.</summary>
    public void ImportParameters(ReadOnlySpan<float> parameters) => _actor.ImportParameters(parameters);
}
