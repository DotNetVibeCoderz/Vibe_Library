// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Spaces;

namespace RLNet.Environments.MultiAgent;

/// <summary>Outcome of one simultaneous step by every agent.</summary>
/// <param name="Terminated">The joint episode reached a terminal state.</param>
/// <param name="Truncated">The joint episode hit the step limit.</param>
/// <remarks>
/// Rewards are per agent and are exposed through
/// <see cref="IMultiAgentEnvironment.LastRewards"/> rather than carried here, so that reading
/// them costs no allocation on a step that happens millions of times.
/// </remarks>
public readonly record struct MultiAgentStepResult(bool Terminated, bool Truncated)
{
    /// <summary>Whether the joint episode is over for any reason.</summary>
    public bool Done => Terminated || Truncated;
}

/// <summary>
/// An environment several agents act in at once, each with its own observation and reward.
/// </summary>
/// <remarks>
/// <para>
/// The model is simultaneous-move and fully synchronous: every agent submits an action, the
/// world advances once, and everyone receives a reward. Turn-based games need a different
/// contract and are out of scope here.
/// </para>
/// <para>
/// Agents keep separate observations because partial observability is the interesting part of
/// the multi-agent setting — a predator that can see the whole board is solving a different,
/// much easier problem than one that can see only its neighbourhood.
/// </para>
/// <para>
/// Note what this interface does <em>not</em> promise: stationarity. Every other agent is part
/// of this one's environment, and they are all learning at once, so the transition dynamics each
/// agent experiences shift under it as the others improve. That breaks the assumption behind
/// every single-agent convergence result. <see cref="RLNet.Agents.IndependentLearners"/> ignores the
/// problem deliberately and documents what that costs.
/// </para>
/// </remarks>
public interface IMultiAgentEnvironment
{
    /// <summary>Short display name.</summary>
    string Name { get; }

    /// <summary>Number of agents acting each step.</summary>
    int AgentCount { get; }

    /// <summary>Observation space, shared by every agent.</summary>
    Space ObservationSpace { get; }

    /// <summary>Action space, shared by every agent.</summary>
    DiscreteSpace ActionSpace { get; }

    /// <summary>Steps elapsed in the current episode.</summary>
    int ElapsedSteps { get; }

    /// <summary>Step limit after which the environment reports truncation, or 0 for none.</summary>
    int MaxEpisodeSteps { get; }

    /// <summary>
    /// The observation of one agent. Valid only until the next <see cref="Reset"/> or
    /// <see cref="Step"/>, like every observation span in RLNet.
    /// </summary>
    ReadOnlySpan<float> ObservationOf(int agent);

    /// <summary>Rewards from the most recent step, indexed by agent.</summary>
    ReadOnlySpan<float> LastRewards { get; }

    /// <summary>Display label for one agent, used by the visualizer.</summary>
    string AgentName(int agent);

    /// <summary>Starts a new episode, optionally re-seeding for reproducibility.</summary>
    void Reset(int? seed = null);

    /// <summary>Advances the world by one simultaneous action per agent.</summary>
    MultiAgentStepResult Step(ReadOnlySpan<int> actions);
}
