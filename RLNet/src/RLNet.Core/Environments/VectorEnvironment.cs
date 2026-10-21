// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Spaces;

namespace RLNet.Environments;

/// <summary>
/// Several copies of one environment stepped together, with their observations in one
/// contiguous buffer and automatic reset when an episode ends.
/// </summary>
/// <remarks>
/// <para>
/// Two things come from it. An on-policy agent acting for eight environments makes one forward
/// pass over a batch of eight instead of eight passes over a batch of one, which makes collection
/// cheaper — though for PPO at its defaults the ten-epoch update dominates, and the measured gain
/// end to end is around 10% (see <c>docs/en/09-benchmarks.md</c>). The larger benefit is the
/// sample: eight trajectories at different points in their episodes are a far less redundant
/// rollout than one trajectory eight times as long.
/// </para>
/// <para>
/// <b>Two observation buffers.</b> When a copy's episode ends it is reset immediately, so
/// <see cref="Observations"/> already holds the first state of its next episode — which is what
/// the next action should be chosen from. The transition that just happened ended somewhere else,
/// and that state is in <see cref="NextObservations"/>. A truncated episode bootstraps from it;
/// treating the reset state as the successor would credit the agent with a fresh start it did not
/// earn.
/// </para>
/// <para>
/// <b>Parallel stepping</b> is available and off by default. Every environment here steps in well
/// under a microsecond, far less than it costs to wake a thread-pool worker, so spreading them
/// across cores is slower, not faster — about six times slower for eight LunarLander copies,
/// measured. It pays off for an environment whose own step is expensive
/// — a physics engine, a simulator behind a socket. Measure before turning it on.
/// </para>
/// </remarks>
public abstract class VectorEnvironment
{
    private readonly IEnvironment[] _environments;
    private readonly float[] _observations;
    private readonly float[] _nextObservations;
    private readonly float[] _rewards;
    private readonly bool[] _terminated;
    private readonly bool[] _truncated;
    private readonly Action<int> _stepOne;

    private protected VectorEnvironment(IEnvironment[] environments, bool parallel)
    {
        if (environments.Length == 0)
            throw new ArgumentException("A vector environment needs at least one copy.", nameof(environments));

        int size = environments[0].ObservationSpace.FlatSize;
        foreach (var environment in environments)
        {
            if (environment.ObservationSpace.FlatSize != size || environment.ActionSpace.FlatSize != environments[0].ActionSpace.FlatSize)
                throw new ArgumentException("Every copy must have the same observation and action spaces.", nameof(environments));
        }
        if (environments.Distinct().Count() != environments.Length)
            throw new ArgumentException(
                "Every copy must be a distinct instance; one environment stepped twice per step is not two environments.",
                nameof(environments));

        _environments = environments;
        ObservationSize = size;
        Parallel = parallel;

        _observations = new float[environments.Length * size];
        _nextObservations = new float[environments.Length * size];
        _rewards = new float[environments.Length];
        _terminated = new bool[environments.Length];
        _truncated = new bool[environments.Length];
        _stepOne = StepAndPublish;
    }

    /// <summary>Number of copies.</summary>
    public int Count => _environments.Length;

    /// <summary>Width of one copy's observation.</summary>
    public int ObservationSize { get; }

    /// <summary>Shape of one copy's observation.</summary>
    public Space ObservationSpace => _environments[0].ObservationSpace;

    /// <summary>Display name of the wrapped environment.</summary>
    public string Name => _environments[0].Name;

    /// <summary>Whether copies are stepped on the thread pool.</summary>
    public bool Parallel { get; }

    /// <summary>The copy at <paramref name="index"/>, for rendering or inspection.</summary>
    public IEnvironment this[int index] => _environments[index];

    /// <summary>
    /// Current observation of every copy, <c>[Count, ObservationSize]</c>, after any automatic
    /// reset — the states to act from next. Invalidated by the next step, like a single
    /// environment's observation.
    /// </summary>
    public ReadOnlySpan<float> Observations => _observations;

    /// <summary>
    /// The state each copy's last transition led to, <em>before</em> any automatic reset. Equal to
    /// <see cref="Observations"/> for copies whose episode continues.
    /// </summary>
    public ReadOnlySpan<float> NextObservations => _nextObservations;

    /// <summary>Reward of each copy's last transition.</summary>
    public ReadOnlySpan<float> Rewards => _rewards;

    /// <summary>Whether each copy's last transition reached a terminal state.</summary>
    public ReadOnlySpan<bool> Terminated => _terminated;

    /// <summary>Whether each copy's last transition hit its time limit.</summary>
    public ReadOnlySpan<bool> Truncated => _truncated;

    /// <summary>
    /// Resets every copy. With a <paramref name="seed"/>, copy <c>i</c> is seeded with
    /// <c>seed + i</c> — distinct episodes, reproducible as a set.
    /// </summary>
    public void Reset(int? seed = null)
    {
        for (int i = 0; i < _environments.Length; i++)
        {
            _environments[i].Reset(seed.HasValue ? seed.Value + i : null);
            _environments[i].Observation.CopyTo(Slice(_observations, i));
            _environments[i].Observation.CopyTo(Slice(_nextObservations, i));
            _rewards[i] = 0f;
            _terminated[i] = false;
            _truncated[i] = false;
        }
    }

    /// <summary>Steps every copy with the actions staged by the derived class.</summary>
    private protected void StepAll()
    {
        if (Parallel) System.Threading.Tasks.Parallel.For(0, _environments.Length, _stepOne);
        else for (int i = 0; i < _environments.Length; i++) StepAndPublish(i);
    }

    /// <summary>Steps copy <paramref name="index"/> with its staged action.</summary>
    private protected abstract StepResult StepOne(int index);

    private void StepAndPublish(int index)
    {
        var environment = _environments[index];
        var result = StepOne(index);

        environment.Observation.CopyTo(Slice(_nextObservations, index));
        _rewards[index] = result.Reward;
        _terminated[index] = result.Terminated;
        _truncated[index] = result.Truncated;

        if (result.Done) environment.Reset();
        environment.Observation.CopyTo(Slice(_observations, index));
    }

    private protected IEnvironment EnvironmentAt(int index) => _environments[index];

    private Span<float> Slice(float[] buffer, int index) => buffer.AsSpan(index * ObservationSize, ObservationSize);
}

/// <summary>Several copies of a discrete environment, stepped together.</summary>
public sealed class DiscreteVectorEnvironment : VectorEnvironment
{
    private readonly int[] _actions;

    /// <summary>Wraps existing, distinct environment instances.</summary>
    public DiscreteVectorEnvironment(IReadOnlyList<IDiscreteEnvironment> environments, bool parallel = false)
        : base([.. environments], parallel)
    {
        ActionSpace = environments[0].ActionSpace;
        _actions = new int[environments.Count];
    }

    /// <summary>Builds <paramref name="count"/> copies from a factory.</summary>
    public DiscreteVectorEnvironment(Func<IDiscreteEnvironment> factory, int count, bool parallel = false)
        : this(Enumerable.Range(0, count).Select(_ => factory()).ToArray(), parallel)
    {
    }

    /// <summary>The action space of one copy.</summary>
    public DiscreteSpace ActionSpace { get; }

    /// <summary>Steps every copy, one action each, and resets any whose episode ended.</summary>
    public void Step(ReadOnlySpan<int> actions)
    {
        if (actions.Length != Count)
            throw new ArgumentException($"Expected {Count} actions, got {actions.Length}.", nameof(actions));

        // Staged in an array the step delegate can reach; a span cannot cross into the thread pool.
        actions.CopyTo(_actions);
        StepAll();
    }

    private protected override StepResult StepOne(int index) =>
        ((IDiscreteEnvironment)EnvironmentAt(index)).Step(_actions[index]);
}

/// <summary>Several copies of a continuous environment, stepped together.</summary>
public sealed class ContinuousVectorEnvironment : VectorEnvironment
{
    private readonly float[] _actions;
    private readonly int _actionSize;

    /// <summary>Wraps existing, distinct environment instances.</summary>
    public ContinuousVectorEnvironment(IReadOnlyList<IContinuousEnvironment> environments, bool parallel = false)
        : base([.. environments], parallel)
    {
        ActionSpace = environments[0].ActionSpace;
        _actionSize = ActionSpace.FlatSize;
        _actions = new float[environments.Count * _actionSize];
    }

    /// <summary>Builds <paramref name="count"/> copies from a factory.</summary>
    public ContinuousVectorEnvironment(Func<IContinuousEnvironment> factory, int count, bool parallel = false)
        : this(Enumerable.Range(0, count).Select(_ => factory()).ToArray(), parallel)
    {
    }

    /// <summary>The action space of one copy.</summary>
    public BoxSpace ActionSpace { get; }

    /// <summary>Steps every copy with its slice of <paramref name="actions"/>, <c>[Count, actionSize]</c>.</summary>
    public void Step(ReadOnlySpan<float> actions)
    {
        if (actions.Length != _actions.Length)
            throw new ArgumentException($"Expected {_actions.Length} action values, got {actions.Length}.", nameof(actions));

        actions.CopyTo(_actions);
        StepAll();
    }

    private protected override StepResult StepOne(int index) =>
        ((IContinuousEnvironment)EnvironmentAt(index)).Step(_actions.AsSpan(index * _actionSize, _actionSize));
}
