// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Spaces;

namespace RLNet.Environments;

/// <summary>Outcome of a single environment step.</summary>
/// <param name="Reward">Scalar reward for the transition.</param>
/// <param name="Terminated">The episode reached a real terminal state (goal, crash, bankruptcy).</param>
/// <param name="Truncated">The episode was cut off by a time limit while still viable.</param>
/// <remarks>
/// Splitting termination from truncation is not pedantry: bootstrapping is only correct when
/// the value of the next state is discarded for <em>terminal</em> states. Cutting an episode at
/// a step limit and treating it as terminal teaches the agent that the world ends at step 500,
/// which is the single most common silent bug in hand-rolled RL loops. Every agent here reads
/// <see cref="Terminated"/> — never <see cref="Done"/> — when it forms a bootstrap target.
/// </remarks>
public readonly record struct StepResult(float Reward, bool Terminated, bool Truncated)
{
    /// <summary>Whether the episode is over for any reason. Use for loop control, never for bootstrapping.</summary>
    public bool Done => Terminated || Truncated;
}

/// <summary>Common surface of every RLNet environment.</summary>
/// <remarks>
/// Observations are exposed as a <see cref="ReadOnlySpan{T}"/> over a buffer the environment
/// owns and overwrites, rather than as a freshly allocated array per step. A training run makes
/// millions of steps; returning an array from each one is the difference between a quiet heap
/// and a garbage collector running flat out. The contract that follows from this is strict and
/// deliberate: <b>the span is invalidated by the next <see cref="Reset"/> or step</b>. Anything
/// that must outlive the step — a replay buffer entry, for instance — copies it.
/// </remarks>
public interface IEnvironment
{
    /// <summary>Shape and bounds of the observation.</summary>
    Space ObservationSpace { get; }

    /// <summary>Shape and bounds of the action.</summary>
    Space ActionSpace { get; }

    /// <summary>The current observation. Valid only until the next <see cref="Reset"/> or step.</summary>
    ReadOnlySpan<float> Observation { get; }

    /// <summary>Steps elapsed in the current episode.</summary>
    int ElapsedSteps { get; }

    /// <summary>Hard step limit after which the environment reports truncation, or 0 for none.</summary>
    int MaxEpisodeSteps { get; }

    /// <summary>Short display name, e.g. <c>"CartPole"</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Starts a new episode. Passing a <paramref name="seed"/> re-seeds the environment's
    /// generator, making the episode — and every episode after it — exactly reproducible.
    /// </summary>
    void Reset(int? seed = null);
}

/// <summary>An environment driven by a single action index.</summary>
public interface IDiscreteEnvironment : IEnvironment
{
    /// <summary>The action space, narrowed. Saves every caller a cast.</summary>
    new DiscreteSpace ActionSpace { get; }

    /// <summary>Advances the simulation by one action.</summary>
    StepResult Step(int action);
}

/// <summary>An environment driven by a continuous action vector.</summary>
public interface IContinuousEnvironment : IEnvironment
{
    /// <summary>The action space, narrowed. Saves every caller a cast.</summary>
    new BoxSpace ActionSpace { get; }

    /// <summary>
    /// Advances the simulation by one action. The environment clamps out-of-range values
    /// rather than throwing, matching Gym behaviour.
    /// </summary>
    StepResult Step(ReadOnlySpan<float> action);
}
