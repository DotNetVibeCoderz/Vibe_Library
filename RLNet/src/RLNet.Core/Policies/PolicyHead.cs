// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Neural;
using RLNet.Persistence;
using RLNet.Utils;

namespace RLNet.Policies;

/// <summary>
/// The distribution-specific part of an on-policy actor: how a network output becomes an action,
/// a log-probability and an entropy, and how their gradients flow back.
/// </summary>
/// <remarks>
/// Everything else in PPO and A2C — rollouts, advantages, the clipped objective, the critic — is
/// identical for discrete and continuous actions. Isolating the difference here is what lets one
/// implementation of each algorithm serve both action types.
/// </remarks>
internal abstract class PolicyHead : ICheckpointable
{
    /// <summary>Width of the actor network's output.</summary>
    public abstract int OutputSize { get; }

    /// <summary>Floats stored per action in the rollout.</summary>
    public abstract int ActionSize { get; }

    /// <summary>Draws an action from the distribution an output row describes.</summary>
    /// <param name="features">The actor's final hidden activations for this row.</param>
    /// <param name="stream">Which environment the action is for.</param>
    public abstract float Sample(
        ReadOnlySpan<float> output, ReadOnlySpan<float> features, int stream,
        Span<float> action, FastRandom random, bool deterministic, out float entropy);

    /// <summary>
    /// Scores a stored action under the current output. Caches what the following gradient calls
    /// need, so they must be made for the same row before the next <see cref="Evaluate"/>.
    /// </summary>
    public abstract float Evaluate(ReadOnlySpan<float> output, ReadOnlySpan<float> features, ReadOnlySpan<float> action, out float entropy);

    /// <summary>Accumulates <c>∂(coefficient · log π)</c> for the row last passed to <see cref="Evaluate"/>.</summary>
    public abstract void AccumulateLogProbabilityGradient(Span<float> outputGradient, ReadOnlySpan<float> output, ReadOnlySpan<float> action, float coefficient);

    /// <summary>Accumulates <c>∂(−coefficient · H)</c> for the row last passed to <see cref="Evaluate"/>.</summary>
    public abstract void AccumulateEntropyGradient(Span<float> outputGradient, float entropy, float coefficient);

    /// <summary>
    /// Called before the first exploring action of every rollout. A head whose exploration is
    /// held fixed across a rollout draws it here; the others draw nothing, so their sequence of
    /// random numbers is unaffected.
    /// </summary>
    public virtual void BeginRollout(int streams, FastRandom random) { }

    /// <summary>Clears gradients of any parameters the head owns outside the network.</summary>
    public virtual void BeginUpdate() { }

    /// <summary>Steps any parameters the head owns outside the network.</summary>
    public virtual void ApplyUpdate(float learningRate) { }

    public virtual void WriteState(CheckpointWriter writer) { }

    public virtual void ReadState(CheckpointReader reader) { }
}

/// <summary>Softmax over logits, for a finite action set. The action is stored as its index.</summary>
internal sealed class CategoricalHead(int actionCount) : PolicyHead
{
    private readonly float[] _probabilities = new float[actionCount];

    public override int OutputSize => actionCount;
    public override int ActionSize => 1;

    public override float Sample(
        ReadOnlySpan<float> output, ReadOnlySpan<float> features, int stream,
        Span<float> action, FastRandom random, bool deterministic, out float entropy)
    {
        action[0] = CategoricalPolicy.Sample(output, _probabilities, random, deterministic, out float logProbability);
        entropy = CategoricalPolicy.Entropy(_probabilities);
        return logProbability;
    }

    public override float Evaluate(ReadOnlySpan<float> output, ReadOnlySpan<float> features, ReadOnlySpan<float> action, out float entropy)
    {
        output.CopyTo(_probabilities);
        SimdOps.SoftmaxInPlace(_probabilities);
        entropy = CategoricalPolicy.Entropy(_probabilities);
        return MathF.Log(MathF.Max(_probabilities[(int)action[0]], 1e-8f));
    }

    public override void AccumulateLogProbabilityGradient(Span<float> outputGradient, ReadOnlySpan<float> output, ReadOnlySpan<float> action, float coefficient) =>
        CategoricalPolicy.AccumulateLogProbabilityGradient(outputGradient, _probabilities, (int)action[0], coefficient);

    public override void AccumulateEntropyGradient(Span<float> outputGradient, float entropy, float coefficient) =>
        CategoricalPolicy.AccumulateEntropyGradient(outputGradient, _probabilities, entropy, coefficient);
}

/// <summary>
/// Diagonal Gaussian with the network emitting the mean and a free, state-independent log
/// standard deviation per dimension. Actions are on the unit scale, unclipped.
/// </summary>
internal sealed class GaussianHead : PolicyHead
{
    // Bounds on the free log-std. Below the floor the policy is effectively deterministic and
    // log-probabilities blow up; above the ceiling it is sampling far outside the action bounds.
    private const float LogStdMin = -5f;
    private const float LogStdMax = 2f;

    private readonly float[] _logStd;
    private readonly float[] _logStdGradient;
    private readonly AdamOptimizer _optimizer;

    public GaussianHead(int actionSize, float initialLogStd, float learningRate)
    {
        _logStd = new float[actionSize];
        _logStd.AsSpan().Fill(initialLogStd);
        _logStdGradient = new float[actionSize];
        _optimizer = new AdamOptimizer(actionSize, learningRate);
    }

    public override int OutputSize => _logStd.Length;
    public override int ActionSize => _logStd.Length;

    /// <summary>Current log standard deviations.</summary>
    public ReadOnlySpan<float> LogStd => _logStd;

    public override float Sample(
        ReadOnlySpan<float> output, ReadOnlySpan<float> features, int stream,
        Span<float> action, FastRandom random, bool deterministic, out float entropy)
    {
        entropy = GaussianPolicy.Entropy(_logStd);
        return GaussianPolicy.Sample(output, _logStd, action, random, deterministic);
    }

    public override float Evaluate(ReadOnlySpan<float> output, ReadOnlySpan<float> features, ReadOnlySpan<float> action, out float entropy)
    {
        entropy = GaussianPolicy.Entropy(_logStd);
        return GaussianPolicy.LogProbability(output, _logStd, action);
    }

    public override void AccumulateLogProbabilityGradient(Span<float> outputGradient, ReadOnlySpan<float> output, ReadOnlySpan<float> action, float coefficient) =>
        GaussianPolicy.AccumulateLogProbabilityGradient(outputGradient, _logStdGradient, output, _logStd, action, coefficient);

    public override void AccumulateEntropyGradient(Span<float> outputGradient, float entropy, float coefficient) =>
        GaussianPolicy.AccumulateEntropyGradient(_logStdGradient, coefficient);

    public override void BeginUpdate() => Array.Clear(_logStdGradient);

    public override void ApplyUpdate(float learningRate)
    {
        _optimizer.LearningRate = learningRate;
        var scope = _optimizer.BeginUpdate();
        scope.Apply(_logStd, _logStdGradient);
        for (int i = 0; i < _logStd.Length; i++) _logStd[i] = Math.Clamp(_logStd[i], LogStdMin, LogStdMax);
    }

    public override void WriteState(CheckpointWriter writer)
    {
        writer.Write("logStd", _logStd);
        writer.Write("logStdOptimizer", _optimizer);
    }

    public override void ReadState(CheckpointReader reader)
    {
        reader.Read("logStd", _logStd);
        reader.Read("logStdOptimizer", _optimizer);
    }
}

/// <summary>
/// Generalised state-dependent exploration (gSDE; Raffin, Hill, Gleave et al., 2022): the
/// exploration noise is a function of the state, held fixed for a whole rollout.
/// </summary>
/// <remarks>
/// <para>
/// A plain Gaussian policy draws fresh, independent noise on every step. Over a rollout that
/// averages to almost nothing — a joint pushed left, then right, then left — and on tasks where
/// progress needs a consistent push for many steps, the policy gradient sees mostly jitter. gSDE
/// draws a random linear map <c>ε</c> from the actor's final hidden features to the action once
/// per rollout, and explores with <c>a = μ(s) + φ(s)·ε</c>: the same state always gets the same
/// perturbation, so a trajectory explores one coherent alternative behaviour instead of noise.
/// </para>
/// <para>
/// Per action dimension the action is then Gaussian with variance <c>Σᵢ φᵢ² σᵢⱼ²</c>, and that
/// is the distribution the log-probabilities and entropies here are taken under. The spreads
/// <c>σ</c> are learned, one per feature and action, through their logarithms. The features are
/// treated as constants — the gradient does not flow from the exploration into the actor's
/// hidden layers — as in the reference implementation.
/// </para>
/// <para>
/// Each stream has its own <c>ε</c>, so eight environments explore eight different behaviours.
/// </para>
/// </remarks>
internal sealed class StateDependentHead : PolicyHead
{
    private const float LogStdMin = -5f;
    private const float LogStdMax = 2f;

    // A floor on each dimension's variance, for a state whose features are all near zero.
    private const float MinVariance = 1e-6f;
    private static readonly float LogTwoPi = MathF.Log(2f * MathF.PI);

    private readonly int _actionSize;
    private readonly int _featureSize;
    private readonly float[] _logStd;            // [featureSize, actionSize]
    private readonly float[] _logStdGradient;
    private readonly AdamOptimizer _optimizer;

    private float[] _exploration = [];           // [streams, featureSize, actionSize], drawn per rollout

    // Evaluate's cache, read by the gradient calls that follow it.
    private readonly float[] _features;
    private readonly float[] _variance;

    public StateDependentHead(int actionSize, int featureSize, float initialLogStd, float learningRate)
    {
        _actionSize = actionSize;
        _featureSize = featureSize;
        _logStd = new float[featureSize * actionSize];
        _logStd.AsSpan().Fill(initialLogStd);
        _logStdGradient = new float[_logStd.Length];
        _optimizer = new AdamOptimizer(_logStd.Length, learningRate);
        _features = new float[featureSize];
        _variance = new float[actionSize];
    }

    public override int OutputSize => _actionSize;
    public override int ActionSize => _actionSize;

    /// <summary>Current log spreads, <c>[featureSize, actionSize]</c>. Writable for the gradient tests.</summary>
    public Span<float> LogStd => _logStd;

    /// <summary>Gradient accumulated since <see cref="BeginUpdate"/>, for the gradient tests.</summary>
    public ReadOnlySpan<float> LogStdGradient => _logStdGradient;

    public override void BeginRollout(int streams, FastRandom random)
    {
        int size = _featureSize * _actionSize;
        if (_exploration.Length != streams * size) _exploration = new float[streams * size];

        for (int stream = 0; stream < streams; stream++)
            for (int k = 0; k < size; k++)
                _exploration[stream * size + k] = MathF.Exp(_logStd[k]) * random.NextGaussian();
    }

    public override float Sample(
        ReadOnlySpan<float> output, ReadOnlySpan<float> features, int stream,
        Span<float> action, FastRandom random, bool deterministic, out float entropy)
    {
        Span<float> variance = stackalloc float[_actionSize];
        Variance(features, variance);
        entropy = Entropy(variance);

        if (deterministic || _exploration.Length == 0)
        {
            output.CopyTo(action);
            return LogProbability(variance, deviation: default);
        }

        var weights = _exploration.AsSpan(stream * _featureSize * _actionSize, _featureSize * _actionSize);
        Span<float> deviation = stackalloc float[_actionSize];
        for (int j = 0; j < _actionSize; j++)
        {
            float noise = 0f;
            for (int i = 0; i < _featureSize; i++) noise += features[i] * weights[i * _actionSize + j];
            deviation[j] = noise;
            action[j] = output[j] + noise;
        }

        return LogProbability(variance, deviation);
    }

    public override float Evaluate(ReadOnlySpan<float> output, ReadOnlySpan<float> features, ReadOnlySpan<float> action, out float entropy)
    {
        features.CopyTo(_features);
        Variance(features, _variance);
        entropy = Entropy(_variance);

        Span<float> deviation = stackalloc float[_actionSize];
        for (int j = 0; j < _actionSize; j++) deviation[j] = action[j] - output[j];
        return LogProbability(_variance, deviation);
    }

    public override void AccumulateLogProbabilityGradient(Span<float> outputGradient, ReadOnlySpan<float> output, ReadOnlySpan<float> action, float coefficient)
    {
        for (int j = 0; j < _actionSize; j++)
        {
            float deviation = action[j] - output[j];
            float inverseVariance = 1f / _variance[j];

            // ∂ log N(a; μ, v) / ∂μ = (a − μ) / v
            outputGradient[j] += coefficient * deviation * inverseVariance;

            // ∂/∂v = ½ (z² − 1) / v, and ∂v/∂log σᵢⱼ = 2 φᵢ² σᵢⱼ², so the product is
            // (z² − 1) · φᵢ² σᵢⱼ² / v — each spread credited by its share of the variance.
            float z2MinusOne = deviation * deviation * inverseVariance - 1f;
            for (int i = 0; i < _featureSize; i++)
            {
                int k = i * _actionSize + j;
                float share = _features[i] * _features[i] * MathF.Exp(2f * _logStd[k]) * inverseVariance;
                _logStdGradient[k] += coefficient * z2MinusOne * share;
            }
        }
    }

    public override void AccumulateEntropyGradient(Span<float> outputGradient, float entropy, float coefficient)
    {
        // H = Σⱼ ½ log(2πe vⱼ), so ∂H/∂log σᵢⱼ is the same variance share as above.
        for (int j = 0; j < _actionSize; j++)
        {
            float inverseVariance = 1f / _variance[j];
            for (int i = 0; i < _featureSize; i++)
            {
                int k = i * _actionSize + j;
                _logStdGradient[k] -= coefficient * _features[i] * _features[i] * MathF.Exp(2f * _logStd[k]) * inverseVariance;
            }
        }
    }

    public override void BeginUpdate() => Array.Clear(_logStdGradient);

    public override void ApplyUpdate(float learningRate)
    {
        _optimizer.LearningRate = learningRate;
        var scope = _optimizer.BeginUpdate();
        scope.Apply(_logStd, _logStdGradient);
        for (int i = 0; i < _logStd.Length; i++) _logStd[i] = Math.Clamp(_logStd[i], LogStdMin, LogStdMax);
    }

    /// <remarks>
    /// The exploration matrices are not saved: like a half-collected rollout they belong to the
    /// rollout in progress, and the next one draws fresh ones.
    /// </remarks>
    public override void WriteState(CheckpointWriter writer)
    {
        writer.Write("sdeLogStd", _logStd);
        writer.Write("sdeOptimizer", _optimizer);
    }

    public override void ReadState(CheckpointReader reader)
    {
        reader.Read("sdeLogStd", _logStd);
        reader.Read("sdeOptimizer", _optimizer);
    }

    private void Variance(ReadOnlySpan<float> features, Span<float> variance)
    {
        for (int j = 0; j < _actionSize; j++)
        {
            float v = MinVariance;
            for (int i = 0; i < _featureSize; i++)
                v += features[i] * features[i] * MathF.Exp(2f * _logStd[i * _actionSize + j]);
            variance[j] = v;
        }
    }

    private static float Entropy(ReadOnlySpan<float> variance)
    {
        float entropy = 0f;
        foreach (float v in variance) entropy += 0.5f * (LogTwoPi + 1f + MathF.Log(v));
        return entropy;
    }

    /// <summary>Σⱼ log N(μⱼ + dⱼ; μⱼ, vⱼ). An empty deviation means the mean itself.</summary>
    private static float LogProbability(ReadOnlySpan<float> variance, ReadOnlySpan<float> deviation)
    {
        float logProbability = 0f;
        for (int j = 0; j < variance.Length; j++)
        {
            float d = deviation.IsEmpty ? 0f : deviation[j];
            logProbability += -0.5f * (d * d / variance[j] + MathF.Log(variance[j]) + LogTwoPi);
        }
        return logProbability;
    }
}
