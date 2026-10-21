// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Persistence;
using RLNet.Spaces;

namespace RLNet.Agents;

/// <summary>
/// Common surface of <see cref="PpoAgent"/> and <see cref="A2CAgent"/>: single or batched
/// action selection over a finite action set, and checkpointing.
/// </summary>
/// <remarks>Not intended for derivation outside the library; the constructor is internal.</remarks>
public abstract class DiscreteOnPolicyAgent : IVectorDiscreteAgent, ICheckpointable
{
    private readonly OnPolicyLearner _learner;
    private float[] _actionScratch = new float[1];

    private protected DiscreteOnPolicyAgent(OnPolicyLearner learner) => _learner = learner;

    public abstract string Name { get; }
    public AgentMetrics Metrics => _learner.Metrics;

    public void SetProgress(float progress) => _learner.SetProgress(progress);

    public void OnEpisodeEnd() { }

    public int SelectAction(ReadOnlySpan<float> observation, bool deterministic = false)
    {
        _learner.Act(observation, 1, _actionScratch.AsSpan(0, 1), deterministic);
        return (int)_actionScratch[0];
    }

    public void Observe(
        ReadOnlySpan<float> observation,
        int action,
        float reward,
        ReadOnlySpan<float> nextObservation,
        bool terminated,
        bool truncated)
    {
        Span<float> encoded = [action];
        _learner.Observe(observation, encoded, [reward], nextObservation, [terminated], [truncated]);
    }

    public void SelectActions(ReadOnlySpan<float> observations, Span<int> actions, bool deterministic = false)
    {
        if (_actionScratch.Length < actions.Length) _actionScratch = new float[actions.Length];

        var encoded = _actionScratch.AsSpan(0, actions.Length);
        _learner.Act(observations, actions.Length, encoded, deterministic);
        for (int i = 0; i < actions.Length; i++) actions[i] = (int)encoded[i];
    }

    public void ObserveBatch(
        ReadOnlySpan<float> observations,
        ReadOnlySpan<int> actions,
        ReadOnlySpan<float> rewards,
        ReadOnlySpan<float> nextObservations,
        ReadOnlySpan<bool> terminated,
        ReadOnlySpan<bool> truncated)
    {
        if (_actionScratch.Length < actions.Length) _actionScratch = new float[actions.Length];

        var encoded = _actionScratch.AsSpan(0, actions.Length);
        for (int i = 0; i < actions.Length; i++) encoded[i] = actions[i];
        _learner.Observe(observations, encoded, rewards, nextObservations, terminated, truncated);
    }

    /// <summary>Copies the actor's parameters out. For a complete, resumable save use <see cref="Checkpoint"/>.</summary>
    public float[] ExportParameters() => _learner.Actor.ExportParameters();

    /// <summary>Loads actor parameters.</summary>
    public void ImportParameters(ReadOnlySpan<float> parameters) => _learner.Actor.ImportParameters(parameters);

    public void WriteState(CheckpointWriter writer) => _learner.WriteState(writer);

    public void ReadState(CheckpointReader reader) => _learner.ReadState(reader);
}

/// <summary>
/// Common surface of <see cref="ContinuousPpoAgent"/> and <see cref="ContinuousA2CAgent"/>: a
/// Gaussian policy over a <see cref="BoxSpace"/>, single or batched, with checkpointing.
/// </summary>
/// <remarks>
/// <para>
/// The policy samples on a unit scale and the sample is stored <em>unclipped</em>; only the copy
/// handed to the environment is clipped to <c>[-1, 1]</c> and rescaled to the action bounds. The
/// log-probability PPO's ratio depends on is the density of the action actually drawn, and a
/// clipped value would not be that action.
/// </para>
/// <para>
/// One consequence: <see cref="Observe"/> records the sample from the preceding
/// <see cref="SelectAction"/>, not the <c>action</c> argument, which is the clipped and rescaled
/// copy. Call them in pairs, as <see cref="Training.Trainer"/> does.
/// </para>
/// <para>Not intended for derivation outside the library; the constructor is internal.</para>
/// </remarks>
public abstract class ContinuousOnPolicyAgent : IVectorContinuousAgent, ICheckpointable
{
    private readonly OnPolicyLearner _learner;
    private readonly BoxSpace _actionSpace;
    private readonly int _actionSize;
    private float[] _unitActions;

    private protected ContinuousOnPolicyAgent(OnPolicyLearner learner, BoxSpace actionSpace)
    {
        _learner = learner;
        _actionSpace = actionSpace;
        _actionSize = actionSpace.FlatSize;
        _unitActions = new float[_actionSize];
    }

    public abstract string Name { get; }
    public AgentMetrics Metrics => _learner.Metrics;

    public void SetProgress(float progress) => _learner.SetProgress(progress);

    public void OnEpisodeEnd() { }

    public void SelectAction(ReadOnlySpan<float> observation, Span<float> action, bool deterministic = false) =>
        SelectActions(observation, action, deterministic);

    public void Observe(
        ReadOnlySpan<float> observation,
        ReadOnlySpan<float> action,
        float reward,
        ReadOnlySpan<float> nextObservation,
        bool terminated,
        bool truncated) =>
        ObserveBatch(observation, action, [reward], nextObservation, [terminated], [truncated]);

    public void SelectActions(ReadOnlySpan<float> observations, Span<float> actions, bool deterministic = false)
    {
        int count = actions.Length / _actionSize;
        if (_unitActions.Length < actions.Length) _unitActions = new float[actions.Length];

        var unit = _unitActions.AsSpan(0, actions.Length);
        _learner.Act(observations, count, unit, deterministic);

        unit.CopyTo(actions);
        for (int i = 0; i < count; i++)
        {
            var action = actions.Slice(i * _actionSize, _actionSize);
            for (int j = 0; j < _actionSize; j++) action[j] = Math.Clamp(action[j], -1f, 1f);
            _actionSpace.ScaleFromUnit(action);
        }
    }

    public void ObserveBatch(
        ReadOnlySpan<float> observations,
        ReadOnlySpan<float> actions,
        ReadOnlySpan<float> rewards,
        ReadOnlySpan<float> nextObservations,
        ReadOnlySpan<bool> terminated,
        ReadOnlySpan<bool> truncated)
    {
        // The unclipped samples recorded by the matching SelectActions, not the clipped copies
        // the environment received; see the class remarks.
        int count = rewards.Length;
        if (_unitActions.Length < count * _actionSize) _unitActions = new float[count * _actionSize];

        var unit = _unitActions.AsSpan(0, count * _actionSize);
        for (int i = 0; i < count; i++) _learner.PendingAction(i).CopyTo(unit.Slice(i * _actionSize, _actionSize));

        _learner.Observe(observations, unit, rewards, nextObservations, terminated, truncated);
    }

    /// <summary>Copies the actor's parameters out. For a complete, resumable save use <see cref="Checkpoint"/>.</summary>
    public float[] ExportParameters() => _learner.Actor.ExportParameters();

    /// <summary>Loads actor parameters.</summary>
    public void ImportParameters(ReadOnlySpan<float> parameters) => _learner.Actor.ImportParameters(parameters);

    public void WriteState(CheckpointWriter writer) => _learner.WriteState(writer);

    public void ReadState(CheckpointReader reader) => _learner.ReadState(reader);
}
