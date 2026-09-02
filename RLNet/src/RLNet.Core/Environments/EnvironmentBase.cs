// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Spaces;
using RLNet.Utils;

namespace RLNet.Environments;

/// <summary>
/// Shared plumbing for environments: the observation buffer, the seeded generator, the step
/// counter and the time limit.
/// </summary>
/// <remarks>
/// Concrete environments override <see cref="OnReset"/> and a step method, and write their
/// observation through <see cref="ObservationBuffer"/>. Keeping the counter and the time-limit
/// check here means no environment can forget to report truncation, which is exactly the bug
/// the terminated/truncated split exists to prevent.
/// </remarks>
public abstract class EnvironmentBase : IEnvironment
{
    private readonly float[] _observation;

    /// <summary>The environment's own generator. Re-seeded by <see cref="Reset"/>.</summary>
    protected FastRandom Random { get; } = new();

    /// <summary>Writable view of the observation the environment publishes.</summary>
    protected Span<float> ObservationBuffer => _observation;

    protected EnvironmentBase(Space observationSpace, Space actionSpace, int maxEpisodeSteps)
    {
        ObservationSpace = observationSpace;
        ActionSpace = actionSpace;
        MaxEpisodeSteps = maxEpisodeSteps;
        _observation = new float[observationSpace.FlatSize];
    }

    public Space ObservationSpace { get; }
    public Space ActionSpace { get; }
    public ReadOnlySpan<float> Observation => _observation;
    public int ElapsedSteps { get; private set; }
    public int MaxEpisodeSteps { get; }
    public abstract string Name { get; }

    public void Reset(int? seed = null)
    {
        if (seed.HasValue) Random.Seed(seed.Value);
        ElapsedSteps = 0;
        OnReset();
        WriteObservation(_observation);
    }

    /// <summary>Puts the simulation into a fresh start state.</summary>
    protected abstract void OnReset();

    /// <summary>Copies the current simulation state into the published observation buffer.</summary>
    protected abstract void WriteObservation(Span<float> destination);

    /// <summary>
    /// Runs the shared step bookkeeping around a concrete step: increments the counter, applies
    /// the time limit, and refreshes the observation.
    /// </summary>
    protected StepResult Advance(float reward, bool terminated)
    {
        ElapsedSteps++;

        // A step limit only truncates; it never terminates. An agent that hits the limit while
        // still balancing has not failed, and its value estimate must still be bootstrapped.
        bool truncated = !terminated && MaxEpisodeSteps > 0 && ElapsedSteps >= MaxEpisodeSteps;

        WriteObservation(_observation);
        return new StepResult(reward, terminated, truncated);
    }
}

/// <summary>Base class for environments with a finite action set.</summary>
public abstract class DiscreteEnvironmentBase : EnvironmentBase, IDiscreteEnvironment
{
    protected DiscreteEnvironmentBase(Space observationSpace, DiscreteSpace actionSpace, int maxEpisodeSteps)
        : base(observationSpace, actionSpace, maxEpisodeSteps) => ActionSpace = actionSpace;

    public new DiscreteSpace ActionSpace { get; }

    public StepResult Step(int action)
    {
        // Out-of-range actions are a programming error rather than a modelling choice, so unlike
        // continuous actions (which are clamped) they throw.
        ArgumentOutOfRangeException.ThrowIfNegative(action);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(action, ActionSpace.Count);
        return OnStep(action);
    }

    /// <summary>Applies one validated action and returns the transition through <see cref="EnvironmentBase.Advance"/>.</summary>
    protected abstract StepResult OnStep(int action);
}

/// <summary>Base class for environments with a continuous action vector.</summary>
public abstract class ContinuousEnvironmentBase : EnvironmentBase, IContinuousEnvironment
{
    private readonly float[] _clamped;

    protected ContinuousEnvironmentBase(Space observationSpace, BoxSpace actionSpace, int maxEpisodeSteps)
        : base(observationSpace, actionSpace, maxEpisodeSteps)
    {
        ActionSpace = actionSpace;
        _clamped = new float[actionSpace.FlatSize];
    }

    public new BoxSpace ActionSpace { get; }

    public StepResult Step(ReadOnlySpan<float> action)
    {
        if (action.Length != _clamped.Length)
            throw new ArgumentException($"{Name} expects {_clamped.Length} action values, got {action.Length}.", nameof(action));

        // Clamping rather than throwing matches Gym: a policy that has not yet learned its
        // bounds should be corrected by the environment, not crash the training run.
        action.CopyTo(_clamped);
        ActionSpace.Clamp(_clamped);
        return OnStep(_clamped);
    }

    /// <summary>Applies one clamped action and returns the transition through <see cref="EnvironmentBase.Advance"/>.</summary>
    protected abstract StepResult OnStep(ReadOnlySpan<float> action);
}
