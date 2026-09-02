// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Utils;

namespace RLNet.Neural;

/// <summary>
/// Two independent action-value networks over <c>(state, action)</c>, with their target copies.
/// </summary>
/// <remarks>
/// <para>
/// Both continuous-control agents in RLNet are built on this, and for the same reason. A single
/// critic trained by bootstrapping systematically <em>overestimates</em>: the target takes a
/// maximum (or a policy trained to maximise) over the critic's own noisy output, and the maximum
/// of noise is biased upward. The actor then exploits the states where the critic is most wrong,
/// which drives the error higher still.
/// </para>
/// <para>
/// Two critics initialised differently and trained on the same targets make independent errors,
/// so taking the smaller of the two is a pessimistic estimate — biased downward instead, which
/// is the harmless direction. That single change is most of what separates TD3 from DDPG, and
/// SAC adopted it for the same reason.
/// </para>
/// <para>
/// Both critics take a concatenated <c>[state, action]</c> input. Actions are always on the unit
/// scale the actor emits — <c>[-1, 1]</c> from a tanh — never in the environment's units, so no
/// agent has to differentiate through the rescaling.
/// </para>
/// </remarks>
public sealed class QNetworkPair
{
    private readonly MlpNetwork _q1;
    private readonly MlpNetwork _q2;
    private readonly MlpNetwork _q1Target;
    private readonly MlpNetwork _q2Target;
    private readonly AdamOptimizer _optimizer1;
    private readonly AdamOptimizer _optimizer2;

    private readonly int _observationSize;
    private readonly int _actionSize;

    // Gradient with respect to the action half of the input, which is what an actor needs and
    // the only reason MlpNetwork exposes a gradient at its input at all.
    private readonly float[] _actionGradient;

    // Scratch for the min-routing in ActionGradientOfMinimum. Preallocated because that method
    // runs on every actor update, which is every gradient step of a training run.
    private readonly float[] _q1Scratch;
    private readonly bool[] _useFirst;

    public QNetworkPair(
        int observationSize,
        int actionSize,
        ReadOnlySpan<int> hiddenSizes,
        float learningRate,
        int maxBatch,
        FastRandom random,
        float maxGradientNorm = 0f,
        IComputeBackend? backend = null)
    {
        _observationSize = observationSize;
        _actionSize = actionSize;

        int inputSize = observationSize + actionSize;

        _q1 = new MlpNetwork(inputSize, hiddenSizes, 1, Activation.ReLU, Activation.Linear, maxBatch, random, 1f, backend);
        _q2 = new MlpNetwork(inputSize, hiddenSizes, 1, Activation.ReLU, Activation.Linear, maxBatch, random, 1f, backend);
        _q1Target = new MlpNetwork(inputSize, hiddenSizes, 1, Activation.ReLU, Activation.Linear, maxBatch, random, 1f, backend);
        _q2Target = new MlpNetwork(inputSize, hiddenSizes, 1, Activation.ReLU, Activation.Linear, maxBatch, random, 1f, backend);

        _q1Target.CopyFrom(_q1);
        _q2Target.CopyFrom(_q2);

        _optimizer1 = new AdamOptimizer(_q1.ParameterCount, learningRate, maxGradientNorm: maxGradientNorm);
        _optimizer2 = new AdamOptimizer(_q2.ParameterCount, learningRate, maxGradientNorm: maxGradientNorm);

        _actionGradient = new float[maxBatch * actionSize];
        _q1Scratch = new float[maxBatch];
        _useFirst = new bool[maxBatch];
    }

    /// <summary>Width of the concatenated critic input.</summary>
    public int InputSize => _observationSize + _actionSize;

    /// <summary>Writes <c>[state, action]</c> pairs into a network's input buffer.</summary>
    private void Pack(MlpNetwork network, ReadOnlySpan<float> observations, ReadOnlySpan<float> actions, int batch)
    {
        var input = network.InputBuffer(batch);
        for (int i = 0; i < batch; i++)
        {
            observations.Slice(i * _observationSize, _observationSize)
                .CopyTo(input.Slice(i * InputSize, _observationSize));
            actions.Slice(i * _actionSize, _actionSize)
                .CopyTo(input.Slice(i * InputSize + _observationSize, _actionSize));
        }
    }

    /// <summary>
    /// Evaluates both target critics and writes the elementwise minimum into
    /// <paramref name="destination"/>.
    /// </summary>
    public void EvaluateTargetMinimum(
        ReadOnlySpan<float> observations,
        ReadOnlySpan<float> actions,
        int batch,
        Span<float> destination)
    {
        Pack(_q1Target, observations, actions, batch);
        var values1 = _q1Target.Forward(batch);
        values1[..batch].CopyTo(destination);

        Pack(_q2Target, observations, actions, batch);
        var values2 = _q2Target.Forward(batch);

        for (int i = 0; i < batch; i++)
            destination[i] = MathF.Min(destination[i], values2[i]);
    }

    /// <summary>
    /// Regresses both critics onto <paramref name="targets"/> and returns the mean squared error
    /// across the pair, along with the first critic's TD errors for prioritised replay.
    /// </summary>
    /// <param name="weights">
    /// Per-sample importance-sampling weights, or an empty span to weight every sample equally.
    /// </param>
    public float Update(
        ReadOnlySpan<float> observations,
        ReadOnlySpan<float> actions,
        ReadOnlySpan<float> targets,
        int batch,
        ReadOnlySpan<float> weights,
        Span<float> tdErrors)
    {
        float loss = UpdateOne(_q1, _optimizer1, observations, actions, targets, batch, weights, tdErrors);
        loss += UpdateOne(_q2, _optimizer2, observations, actions, targets, batch, weights, default);
        return loss * 0.5f;
    }

    private float UpdateOne(
        MlpNetwork critic,
        AdamOptimizer optimizer,
        ReadOnlySpan<float> observations,
        ReadOnlySpan<float> actions,
        ReadOnlySpan<float> targets,
        int batch,
        ReadOnlySpan<float> weights,
        Span<float> tdErrors)
    {
        Pack(critic, observations, actions, batch);
        var values = critic.Forward(batch);

        var gradient = critic.OutputGradientBuffer(batch);
        float loss = 0f;

        for (int i = 0; i < batch; i++)
        {
            float error = values[i] - targets[i];
            float weight = weights.IsEmpty ? 1f : weights[i];

            loss += 0.5f * error * error * weight;
            gradient[i] = error * weight;

            if (!tdErrors.IsEmpty) tdErrors[i] = error;
        }

        critic.ZeroGradients();
        critic.Backward(batch);
        critic.ApplyGradients(optimizer, 1f / batch);

        return loss / batch;
    }

    /// <summary>
    /// Returns <c>d min(Q1, Q2) / d action</c> for each sample — the signal an actor ascends.
    /// </summary>
    /// <remarks>
    /// Only the smaller critic of each sample contributes, matching the minimum taken in the
    /// loss. Both critics' parameter gradients are discarded afterwards: this pass exists purely
    /// to move gradient through to the action, and letting it reach the critics' weights would
    /// train them to make their own output <em>larger</em>, which is not a thing a critic should
    /// ever be trained to do.
    /// </remarks>
    public ReadOnlySpan<float> ActionGradientOfMinimum(
        ReadOnlySpan<float> observations,
        ReadOnlySpan<float> actions,
        int batch)
    {
        Pack(_q1, observations, actions, batch);
        _q1.Forward(batch)[..batch].CopyTo(_q1Scratch);

        Pack(_q2, observations, actions, batch);
        var values2 = _q2.Forward(batch);

        // Route each sample's gradient to whichever critic reported the smaller value.
        for (int i = 0; i < batch; i++) _useFirst[i] = _q1Scratch[i] <= values2[i];

        var gradient2 = _q2.OutputGradientBuffer(batch);
        for (int i = 0; i < batch; i++) gradient2[i] = _useFirst[i] ? 0f : 1f;

        var input2 = _q2.BackwardToInput(batch);
        ExtractActionGradient(input2, batch);

        // No second forward pass for Q1. The two critics are separate networks holding separate
        // activation caches, so running Q2 forward above left Q1's cache from its own pass
        // untouched and still valid to backpropagate through.
        var gradient1 = _q1.OutputGradientBuffer(batch);
        for (int i = 0; i < batch; i++) gradient1[i] = _useFirst[i] ? 1f : 0f;

        var input1 = _q1.BackwardToInput(batch);
        AccumulateActionGradient(input1, batch);

        _q1.ZeroGradients();
        _q2.ZeroGradients();

        return _actionGradient.AsSpan(0, batch * _actionSize);
    }

    private void ExtractActionGradient(ReadOnlySpan<float> inputGradient, int batch)
    {
        for (int i = 0; i < batch; i++)
            inputGradient.Slice(i * InputSize + _observationSize, _actionSize)
                .CopyTo(_actionGradient.AsSpan(i * _actionSize, _actionSize));
    }

    private void AccumulateActionGradient(ReadOnlySpan<float> inputGradient, int batch)
    {
        for (int i = 0; i < batch; i++)
        {
            var source = inputGradient.Slice(i * InputSize + _observationSize, _actionSize);
            var destination = _actionGradient.AsSpan(i * _actionSize, _actionSize);
            for (int j = 0; j < _actionSize; j++) destination[j] += source[j];
        }
    }

    /// <summary>Moves both target critics a fraction <paramref name="tau"/> toward their live counterparts.</summary>
    public void SoftUpdateTargets(float tau)
    {
        _q1Target.SoftUpdateFrom(_q1, tau);
        _q2Target.SoftUpdateFrom(_q2, tau);
    }

    /// <summary>Copies both critics' parameters out.</summary>
    public float[] ExportParameters()
    {
        var buffer = new float[_q1.ParameterCount + _q2.ParameterCount];
        _q1.ExportParameters(buffer.AsSpan(0, _q1.ParameterCount));
        _q2.ExportParameters(buffer.AsSpan(_q1.ParameterCount));
        return buffer;
    }
}
