// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Buffers;
using RLNet.Neural;
using RLNet.Spaces;
using RLNet.Utils;

namespace RLNet.Agents;

/// <summary>Tunable settings for <see cref="DqnAgent"/>.</summary>
public sealed class DqnOptions
{
    /// <summary>Hidden layer widths.</summary>
    public int[] HiddenSizes { get; set; } = [128, 128];

    /// <summary>Adam step size.</summary>
    public float LearningRate { get; set; } = 5e-4f;

    /// <summary>Discount factor.</summary>
    public float Gamma { get; set; } = 0.99f;

    /// <summary>Transitions per gradient step.</summary>
    public int BatchSize { get; set; } = 64;

    /// <summary>Transitions collected before learning begins, so the first batches are not all one episode.</summary>
    public int LearningStarts { get; set; } = 1_000;

    /// <summary>Environment steps between gradient steps.</summary>
    public int TrainFrequency { get; set; } = 1;

    /// <summary>Gradient steps between target-network syncs.</summary>
    public int TargetUpdateInterval { get; set; } = 500;

    /// <summary>Exploration schedule over training progress.</summary>
    public Schedule Epsilon { get; set; } = Schedule.Linear(1f, 0.05f, 0.5f);

    /// <summary>
    /// Use double Q-learning: the online network picks the successor action, the target network
    /// scores it.
    /// </summary>
    public bool DoubleQ { get; set; } = true;

    /// <summary>
    /// Split the head into a state value and an action-advantage stream.
    /// </summary>
    public bool Dueling { get; set; } = true;

    /// <summary>Huber transition point. Above this the loss is linear in the error.</summary>
    public float HuberDelta { get; set; } = 1f;

    /// <summary>Global gradient-norm clip, or 0 to disable.</summary>
    public float MaxGradientNorm { get; set; } = 10f;

    /// <summary>Replay capacity used when the agent builds its own buffer.</summary>
    public int BufferCapacity { get; set; } = 100_000;

    /// <summary>Build a prioritised buffer rather than a uniform one, when the agent builds its own.</summary>
    public bool PrioritizedReplay { get; set; } = true;
}

/// <summary>
/// Deep Q-Network: a neural action-value function trained off-policy from replayed experience.
/// </summary>
/// <remarks>
/// <para>
/// After Mnih et al. (2015), with the two refinements that matter most in practice. <b>Double
/// Q-learning</b> (van Hasselt et al., 2016) splits action selection from action evaluation,
/// because a single network taking a max over its own noisy estimates systematically
/// overestimates — the max of noise is biased upward, and that bias compounds through
/// bootstrapping. <b>Dueling heads</b> (Wang et al., 2016) factor Q into a state value and an
/// advantage, which lets the network learn that a state is bad without having to discover it
/// separately for every action.
/// </para>
/// <para>
/// The two networks are the whole trick. Regressing toward a target computed by the network
/// being updated is a moving target, and it diverges; freezing a copy for a few hundred steps
/// makes the regression stationary enough to converge.
/// </para>
/// </remarks>
public sealed class DqnAgent : IDiscreteAgent
{
    private readonly DqnOptions _options;
    private readonly IReplayBuffer _buffer;
    private readonly ReplayBatch _batch;
    private readonly FastRandom _random;

    private readonly MlpNetwork _online;
    private readonly MlpNetwork _target;
    private readonly AdamOptimizer _optimizer;

    private readonly int _actionCount;
    private readonly int _observationSize;

    // Dueling networks emit [value, advantage_0 .. advantage_n]; this scratch holds the Q-values
    // recombined from them, and doubles as the plain output copy in the non-dueling case.
    private readonly float[] _qScratch;
    private readonly float[] _qNextOnline;
    private readonly float[] _tdErrors;
    private readonly float[] _targetValues;

    private float _progress;
    private int _stepsSinceTrain;
    private int _updatesSinceSync;

    public DqnAgent(
        Space observationSpace,
        DiscreteSpace actionSpace,
        DqnOptions? options = null,
        IReplayBuffer? buffer = null,
        int? seed = null,
        IComputeBackend? backend = null)
    {
        _options = options ?? new DqnOptions();
        _random = seed.HasValue ? new FastRandom(seed.Value) : new FastRandom();

        _observationSize = observationSpace.FlatSize;
        _actionCount = actionSpace.Count;

        _buffer = buffer ?? (_options.PrioritizedReplay
            ? new PrioritizedReplayBuffer(_options.BufferCapacity, _observationSize, 1)
            : new UniformReplayBuffer(_options.BufferCapacity, _observationSize, 1));

        _batch = new ReplayBatch(_options.BatchSize, _observationSize, 1);

        // The dueling head carries one extra unit for the state value.
        int headSize = _options.Dueling ? _actionCount + 1 : _actionCount;

        _online = new MlpNetwork(
            _observationSize, _options.HiddenSizes, headSize,
            Activation.ReLU, Activation.Linear,
            _options.BatchSize, _random, 1f, backend);

        _target = new MlpNetwork(
            _observationSize, _options.HiddenSizes, headSize,
            Activation.ReLU, Activation.Linear,
            _options.BatchSize, _random, 1f, backend);

        _target.CopyFrom(_online);

        _optimizer = new AdamOptimizer(
            _online.ParameterCount, _options.LearningRate, maxGradientNorm: _options.MaxGradientNorm);

        _qScratch = new float[_options.BatchSize * _actionCount];
        _qNextOnline = new float[_options.BatchSize * _actionCount];
        _tdErrors = new float[_options.BatchSize];
        _targetValues = new float[_options.BatchSize];
    }

    public string Name => _options.Dueling ? "Dueling DQN" : "DQN";
    public AgentMetrics Metrics { get; } = new();

    /// <summary>The replay buffer in use, exposed so a caller can inspect or pre-fill it.</summary>
    public IReplayBuffer Buffer => _buffer;

    public void SetProgress(float progress)
    {
        _progress = Math.Clamp(progress, 0f, 1f);
        if (_buffer is PrioritizedReplayBuffer prioritized) prioritized.AnnealBeta(_progress);
    }

    public int SelectAction(ReadOnlySpan<float> observation, bool deterministic = false)
    {
        float epsilon = deterministic ? 0f : _options.Epsilon.At(_progress);
        Metrics.Epsilon = epsilon;

        if (epsilon > 0f && _random.NextSingle() < epsilon)
            return _random.NextInt(_actionCount);

        var raw = _online.Forward(observation);
        ToQValues(raw, _qScratch.AsSpan(0, _actionCount), 1);

        SimdOps.Max(_qScratch.AsSpan(0, _actionCount), out int best);
        return best;
    }

    public void Observe(
        ReadOnlySpan<float> observation,
        int action,
        float reward,
        ReadOnlySpan<float> nextObservation,
        bool terminated,
        bool truncated)
    {
        Span<float> encoded = stackalloc float[1];
        encoded[0] = action;

        // Only true termination is stored. A transition cut off by the step limit still has a
        // future worth bootstrapping through, and recording it as terminal teaches the agent
        // that the world ends at the time limit.
        _buffer.Add(observation, encoded, reward, nextObservation, terminated);

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

        // --- Targets ------------------------------------------------------------------------
        // Everything here is a constant as far as the gradient is concerned, so it is computed
        // before the online network's forward pass overwrites its activation cache.

        if (_options.DoubleQ)
        {
            // Action selection with the online network, evaluation with the target network. Two
            // separate estimates of the same quantity, so their errors do not reinforce.
            _batch.NextObservations.AsSpan(0, batch * _observationSize).CopyTo(_online.InputBuffer(batch));
            ToQValues(_online.Forward(batch), _qNextOnline.AsSpan(0, batch * _actionCount), batch);
        }

        _batch.NextObservations.AsSpan(0, batch * _observationSize).CopyTo(_target.InputBuffer(batch));
        var targetRaw = _target.Forward(batch);

        Span<float> nextQ = _qScratch.AsSpan(0, batch * _actionCount);
        ToQValues(targetRaw, nextQ, batch);

        for (int i = 0; i < batch; i++)
        {
            var row = nextQ.Slice(i * _actionCount, _actionCount);

            float bootstrap;
            if (_options.DoubleQ)
            {
                SimdOps.Max(_qNextOnline.AsSpan(i * _actionCount, _actionCount), out int argmax);
                bootstrap = row[argmax];
            }
            else
            {
                bootstrap = SimdOps.Max(row);
            }

            _targetValues[i] = _batch.Terminated[i]
                ? _batch.Rewards[i]
                : _batch.Rewards[i] + _options.Gamma * bootstrap;
        }

        // --- Prediction and loss ------------------------------------------------------------

        _batch.Observations.AsSpan(0, batch * _observationSize).CopyTo(_online.InputBuffer(batch));
        var onlineRaw = _online.Forward(batch);

        Span<float> predicted = _qScratch.AsSpan(0, batch * _actionCount);
        ToQValues(onlineRaw, predicted, batch);

        var gradient = _online.OutputGradientBuffer(batch);
        gradient.Clear();

        float lossSum = 0f, errorSum = 0f;

        for (int i = 0; i < batch; i++)
        {
            int action = _batch.DiscreteAction(i);
            float error = predicted[i * _actionCount + action] - _targetValues[i];

            _tdErrors[i] = error;
            errorSum += MathF.Abs(error);

            // Huber rather than squared error: quadratic near zero so small errors still carry a
            // proportional signal, linear beyond, so one badly-estimated transition cannot
            // dominate the batch. DQN targets are noisy by construction and this is what keeps
            // that noise from destabilising the whole network.
            float delta = _options.HuberDelta;
            float absError = MathF.Abs(error);

            float gradientValue;
            if (absError <= delta)
            {
                lossSum += 0.5f * error * error;
                gradientValue = error;
            }
            else
            {
                lossSum += delta * (absError - 0.5f * delta);
                gradientValue = delta * MathF.Sign(error);
            }

            // Prioritised replay draws high-error transitions more often, which biases the
            // expected update; the importance-sampling weight is what pays that back.
            gradientValue *= _batch.Weights[i];

            // Only the action actually taken has a target. Every other action's Q-value is
            // untouched by this transition and must receive exactly zero gradient.
            ScatterActionGradient(gradient, i, action, gradientValue);
        }

        _online.ZeroGradients();
        _online.Backward(batch);
        _online.ApplyGradients(_optimizer, 1f / batch);

        _buffer.UpdatePriorities(_batch.Indices.AsSpan(0, batch), _tdErrors.AsSpan(0, batch));

        Metrics.ValueLoss = lossSum / batch;
        Metrics.TdError = errorSum / batch;
        Metrics.UpdateCount++;

        if (++_updatesSinceSync >= _options.TargetUpdateInterval)
        {
            _updatesSinceSync = 0;
            _target.CopyFrom(_online);
        }
    }

    /// <summary>
    /// Converts a network head into Q-values, recombining the dueling streams when they are in use.
    /// </summary>
    /// <remarks>
    /// The dueling identity is <c>Q(s,a) = V(s) + A(s,a) - mean_a A(s,a)</c>. Subtracting the mean
    /// is not cosmetic: without it V and A are only defined up to a constant that can slide freely
    /// between them, and the two streams drift apart without ever changing Q.
    /// </remarks>
    private void ToQValues(ReadOnlySpan<float> raw, Span<float> q, int batch)
    {
        if (!_options.Dueling)
        {
            raw[..(batch * _actionCount)].CopyTo(q);
            return;
        }

        int head = _actionCount + 1;
        for (int i = 0; i < batch; i++)
        {
            var row = raw.Slice(i * head, head);
            float value = row[0];
            var advantages = row[1..];

            float mean = 0f;
            for (int a = 0; a < _actionCount; a++) mean += advantages[a];
            mean /= _actionCount;

            var destination = q.Slice(i * _actionCount, _actionCount);
            for (int a = 0; a < _actionCount; a++)
                destination[a] = value + advantages[a] - mean;
        }
    }

    /// <summary>
    /// Routes one action's Q-value gradient back into the network's raw output slots.
    /// </summary>
    /// <remarks>
    /// For a plain head that is a single slot. For a dueling head the chain rule through
    /// <c>Q_a = V + A_a - mean(A)</c> gives <c>dQ_a/dV = 1</c> and
    /// <c>dQ_a/dA_j = [j == a] - 1/n</c> — so the value stream takes the full gradient and every
    /// advantage unit takes a share, which is exactly how the value stream learns from actions it
    /// did not take.
    /// </remarks>
    private void ScatterActionGradient(Span<float> gradient, int sample, int action, float value)
    {
        if (!_options.Dueling)
        {
            gradient[sample * _actionCount + action] = value;
            return;
        }

        int head = _actionCount + 1;
        var row = gradient.Slice(sample * head, head);

        row[0] = value;

        float share = value / _actionCount;
        for (int a = 0; a < _actionCount; a++)
            row[1 + a] = (a == action ? value : 0f) - share;
    }

    /// <summary>Copies the online network's parameters out, for saving a trained agent.</summary>
    public float[] ExportParameters() => _online.ExportParameters();

    /// <summary>Loads parameters into both the online and target networks.</summary>
    public void ImportParameters(ReadOnlySpan<float> parameters)
    {
        _online.ImportParameters(parameters);
        _target.CopyFrom(_online);
    }
}
