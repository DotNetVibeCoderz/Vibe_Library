// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace RLNet.Agents;

/// <summary>
/// Live numbers an agent reports about its own learning, for charts and diagnostics.
/// </summary>
/// <remarks>
/// A mutable class updated in place rather than a value returned per step: the visualizer polls
/// it at frame rate while training runs at tens of thousands of steps per second, so allocating
/// a snapshot per step would be pure waste. Fields not meaningful for a given algorithm stay at
/// <see cref="float.NaN"/>, which is how a chart knows not to draw them.
/// </remarks>
public sealed class AgentMetrics
{
    /// <summary>Most recent value or critic loss.</summary>
    public float ValueLoss { get; internal set; } = float.NaN;

    /// <summary>Most recent policy loss. Not meaningful for value-based agents.</summary>
    public float PolicyLoss { get; internal set; } = float.NaN;

    /// <summary>Entropy of the policy, in nats. Falling toward zero means the policy is committing.</summary>
    public float Entropy { get; internal set; } = float.NaN;

    /// <summary>Exploration rate, for epsilon-greedy agents.</summary>
    public float Epsilon { get; internal set; } = float.NaN;

    /// <summary>SAC's entropy temperature.</summary>
    public float Temperature { get; internal set; } = float.NaN;

    /// <summary>Mean absolute TD error over the last batch — how surprised the agent still is.</summary>
    public float TdError { get; internal set; } = float.NaN;

    /// <summary>Gradient steps taken so far.</summary>
    public long UpdateCount { get; internal set; }

    /// <summary>Environment steps observed so far.</summary>
    public long StepCount { get; internal set; }
}

/// <summary>Common surface of every learning agent.</summary>
public interface IAgent
{
    /// <summary>Short display name, e.g. <c>"PPO"</c>.</summary>
    string Name { get; }

    /// <summary>Live learning diagnostics, updated in place.</summary>
    AgentMetrics Metrics { get; }

    /// <summary>
    /// Notifies the agent that an episode ended. On-policy agents use it to close out a
    /// trajectory; epsilon-greedy agents decay their exploration here.
    /// </summary>
    void OnEpisodeEnd();

    /// <summary>
    /// Reports how far through training the run is, in <c>[0, 1]</c>, so schedules that anneal —
    /// learning rate, clip range, prioritised-replay beta — can follow it. Optional; agents
    /// behave sensibly without it.
    /// </summary>
    void SetProgress(float progress);
}

/// <summary>An agent that acts in a <see cref="Environments.IDiscreteEnvironment"/>.</summary>
public interface IDiscreteAgent : IAgent
{
    /// <summary>Chooses an action.</summary>
    /// <param name="deterministic">
    /// Suppresses exploration, for evaluating what the agent has actually learned rather than
    /// what it is still trying. Training always passes false.
    /// </param>
    int SelectAction(ReadOnlySpan<float> observation, bool deterministic = false);

    /// <summary>
    /// Hands the agent one transition. Whether it learns immediately, buffers it, or both is up
    /// to the algorithm.
    /// </summary>
    void Observe(
        ReadOnlySpan<float> observation,
        int action,
        float reward,
        ReadOnlySpan<float> nextObservation,
        bool terminated,
        bool truncated);
}

/// <summary>An agent that acts in a <see cref="Environments.IContinuousEnvironment"/>.</summary>
public interface IContinuousAgent : IAgent
{
    /// <summary>Writes an action into <paramref name="action"/>, already scaled to the environment's bounds.</summary>
    void SelectAction(ReadOnlySpan<float> observation, Span<float> action, bool deterministic = false);

    /// <summary>Hands the agent one transition.</summary>
    /// <param name="action">
    /// The action as passed to the environment. The agent converts back to its own internal
    /// scale, so callers never deal in normalised units.
    /// </param>
    void Observe(
        ReadOnlySpan<float> observation,
        ReadOnlySpan<float> action,
        float reward,
        ReadOnlySpan<float> nextObservation,
        bool terminated,
        bool truncated);
}
