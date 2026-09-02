// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics;
using RLNet.Agents;
using RLNet.Environments;
using RLNet.Environments.MultiAgent;

namespace RLNet.Visualizer;

/// <summary>A fixed-length ring of recent samples, for the recorder traces.</summary>
/// <remarks>
/// A run produces tens of thousands of episodes and the strip only ever draws the last few
/// hundred. A ring keeps memory flat and, more usefully, keeps the chart's work independent of
/// how long training has been going.
/// </remarks>
public sealed class TraceBuffer(int capacity)
{
    private readonly float[] _values = new float[capacity];
    private int _head;

    public int Capacity => capacity;
    public int Count { get; private set; }

    /// <summary>Smallest value currently held, ignoring gaps.</summary>
    public float Minimum { get; private set; } = float.MaxValue;

    /// <summary>Largest value currently held.</summary>
    public float Maximum { get; private set; } = float.MinValue;

    public void Add(float value)
    {
        if (!float.IsFinite(value)) return;

        _values[_head] = value;
        _head = (_head + 1) % capacity;
        if (Count < capacity) Count++;

        // Recomputing the range over the whole ring on every append is O(n) per episode and
        // pointless at this size, but the bounds do have to be rebuilt when a value scrolls out —
        // otherwise an early outlier flattens the trace forever.
        RecomputeBounds();
    }

    /// <summary>Reads the ring oldest-first.</summary>
    public float this[int index] => _values[(_head - Count + index + capacity * 2) % capacity];

    public void Clear()
    {
        _head = 0;
        Count = 0;
        Minimum = float.MaxValue;
        Maximum = float.MinValue;
    }

    private void RecomputeBounds()
    {
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < Count; i++)
        {
            float v = this[i];
            if (v < min) min = v;
            if (v > max) max = v;
        }
        Minimum = min;
        Maximum = max;
    }
}

/// <summary>
/// Drives one environment and one agent, stepping in time-budgeted slices so the console stays
/// responsive at any speed.
/// </summary>
/// <remarks>
/// <para>
/// Training runs on the UI thread rather than a background one. That is a deliberate trade:
/// a background thread would train faster, but the renderer reads live environment state — cart
/// position, lander attitude, the arm's joint angles — and reading that while another thread
/// mutates it is a race that would need every environment to publish an immutable snapshot per
/// step. Time-slicing instead keeps a single thread and stays honest, and
/// <see cref="StepsPerFrame"/> still reaches tens of thousands of steps a second, far past what
/// anyone can watch.
/// </para>
/// <para>
/// <see cref="FrameBudget"/> is the safety valve: however many steps are asked for, the slice
/// stops when the budget is spent, so a slow environment cannot freeze the window.
/// </para>
/// </remarks>
public sealed class TrainingSession
{
    private const int TraceCapacity = 400;

    private readonly float[] _observation;
    private readonly float[] _continuousAction = [];
    private readonly Stopwatch _clock = new();

    private readonly IDiscreteEnvironment? _discreteEnvironment;
    private readonly IContinuousEnvironment? _continuousEnvironment;
    private readonly IMultiAgentEnvironment? _multiAgentEnvironment;

    private readonly IDiscreteAgent? _discreteAgent;
    private readonly IContinuousAgent? _continuousAgent;
    private readonly IndependentLearners? _learners;

    private float _episodeReturn;
    private int _episodeSteps;
    private long _totalSteps;
    private double _stepsPerSecond;
    private long _stepsAtLastSample;
    private readonly Stopwatch _rateClock = Stopwatch.StartNew();

    private TrainingSession(string environmentName, string algorithmName, int observationSize, int actionCount)
    {
        EnvironmentName = environmentName;
        AlgorithmName = algorithmName;
        ActionCount = actionCount;
        _observation = new float[observationSize];
    }

    /// <summary>Builds a session for a discrete environment.</summary>
    public TrainingSession(IDiscreteEnvironment environment, IDiscreteAgent agent, string algorithmName)
        : this(environment.Name, algorithmName, environment.ObservationSpace.FlatSize, environment.ActionSpace.Count)
    {
        _discreteEnvironment = environment;
        _discreteAgent = agent;
        ActionLabels = [.. Enumerable.Range(0, environment.ActionSpace.Count).Select(environment.ActionSpace.LabelOf)];
        environment.Reset(Seed);
    }

    /// <summary>Builds a session for a continuous environment.</summary>
    public TrainingSession(IContinuousEnvironment environment, IContinuousAgent agent, string algorithmName)
        : this(environment.Name, algorithmName, environment.ObservationSpace.FlatSize, 0)
    {
        _continuousEnvironment = environment;
        _continuousAgent = agent;
        _continuousAction = new float[environment.ActionSpace.FlatSize];
        ActionLabels = [.. Enumerable.Range(0, environment.ActionSpace.FlatSize).Select(environment.ActionSpace.LabelOf)];
        environment.Reset(Seed);
    }

    /// <summary>Builds a session for a multi-agent environment.</summary>
    public TrainingSession(IMultiAgentEnvironment environment, IndependentLearners learners, string algorithmName)
        : this(environment.Name, algorithmName, environment.ObservationSpace.FlatSize, environment.ActionSpace.Count)
    {
        _multiAgentEnvironment = environment;
        _learners = learners;
        ActionLabels = [.. Enumerable.Range(0, environment.ActionSpace.Count).Select(environment.ActionSpace.LabelOf)];
        environment.Reset(Seed);
    }

    /// <summary>Seed every session starts from, so a demonstration is repeatable.</summary>
    public const int Seed = 20260902;

    public string EnvironmentName { get; }
    public string AlgorithmName { get; }

    /// <summary>Number of discrete actions, or 0 for a continuous environment.</summary>
    public int ActionCount { get; }

    /// <summary>Display names for the actions, or for the action dimensions when continuous.</summary>
    public IReadOnlyList<string> ActionLabels { get; } = [];

    /// <summary>The environment being rendered, whichever kind it is.</summary>
    public object Environment =>
        (object?)_discreteEnvironment ?? _continuousEnvironment ?? (object)_multiAgentEnvironment!;

    /// <summary>The agent's live diagnostics.</summary>
    public AgentMetrics Metrics =>
        _discreteAgent?.Metrics ?? _continuousAgent?.Metrics ?? _learners!.Agents[0].Metrics;

    /// <summary>Steps to attempt per rendered frame.</summary>
    public int StepsPerFrame { get; set; } = 4;

    /// <summary>How long a single slice may take before it yields to the renderer.</summary>
    public TimeSpan FrameBudget { get; set; } = TimeSpan.FromMilliseconds(8);

    /// <summary>
    /// Training progress in <c>[0, 1]</c>, driving the agents' schedules.
    /// </summary>
    /// <remarks>
    /// The console has no fixed end, so progress is measured against a nominal horizon. Without
    /// something here, every schedule would sit at its starting value forever and epsilon would
    /// never decay — the agent would explore at 100% for the whole demonstration.
    /// </remarks>
    public long NominalHorizon { get; set; } = 200_000;

    public bool IsRunning { get; private set; }

    public int Episode { get; private set; }
    public int EpisodeSteps => _episodeSteps;
    public float EpisodeReturn => _episodeReturn;
    public long TotalSteps => _totalSteps;
    public double StepsPerSecond => _stepsPerSecond;

    /// <summary>Most recent action, for the action lamps. -1 before the first step.</summary>
    public int LastAction { get; private set; } = -1;

    /// <summary>Best episode return so far.</summary>
    public float BestReturn { get; private set; } = float.NaN;

    /// <summary>Mean return over the last 20 episodes, the trend worth reading.</summary>
    public float RecentAverage { get; private set; } = float.NaN;

    /// <summary>Per-episode return.</summary>
    public TraceBuffer ReturnTrace { get; } = new(TraceCapacity);

    /// <summary>Per-episode value or critic loss.</summary>
    public TraceBuffer LossTrace { get; } = new(TraceCapacity);

    /// <summary>
    /// Per-episode exploration: epsilon for value-based agents, policy entropy for the rest.
    /// </summary>
    public TraceBuffer ExplorationTrace { get; } = new(TraceCapacity);

    /// <summary>Label for the exploration trace, which measures a different thing per algorithm.</summary>
    public string ExplorationLabel { get; private set; } = "EXPLORATION";

    public void Start() => IsRunning = true;
    public void Stop() => IsRunning = false;

    /// <summary>
    /// Runs one slice of training. Returns the number of steps actually taken.
    /// </summary>
    public int Advance()
    {
        if (!IsRunning) return 0;

        _clock.Restart();
        int taken = 0;

        while (taken < StepsPerFrame)
        {
            Step();
            taken++;

            // Checking the clock costs a syscall on some platforms, so it is amortised over a
            // block of steps rather than paid on every one.
            if ((taken & 0x3F) == 0 && _clock.Elapsed > FrameBudget) break;
        }

        _totalSteps += taken;
        UpdateRate();
        return taken;
    }

    private void UpdateRate()
    {
        // A short window, so the readout responds to a speed change rather than averaging it away
        // over the whole run.
        if (_rateClock.ElapsedMilliseconds < 250) return;

        _stepsPerSecond = (_totalSteps - _stepsAtLastSample) / _rateClock.Elapsed.TotalSeconds;
        _stepsAtLastSample = _totalSteps;
        _rateClock.Restart();
    }

    private void Step()
    {
        float progress = Math.Clamp(_totalSteps / (float)NominalHorizon, 0f, 1f);

        if (_discreteEnvironment is not null && _discreteAgent is not null)
        {
            _discreteAgent.SetProgress(progress);

            _discreteEnvironment.Observation.CopyTo(_observation);
            int action = _discreteAgent.SelectAction(_observation);
            LastAction = action;

            var step = _discreteEnvironment.Step(action);
            _discreteAgent.Observe(
                _observation, action, step.Reward, _discreteEnvironment.Observation,
                step.Terminated, step.Truncated);

            Accumulate(step.Reward);
            if (step.Done)
            {
                _discreteAgent.OnEpisodeEnd();
                CompleteEpisode();
                _discreteEnvironment.Reset();
            }
        }
        else if (_continuousEnvironment is not null && _continuousAgent is not null)
        {
            _continuousAgent.SetProgress(progress);

            _continuousEnvironment.Observation.CopyTo(_observation);
            _continuousAgent.SelectAction(_observation, _continuousAction);

            var step = _continuousEnvironment.Step(_continuousAction);
            _continuousAgent.Observe(
                _observation, _continuousAction, step.Reward, _continuousEnvironment.Observation,
                step.Terminated, step.Truncated);

            Accumulate(step.Reward);
            if (step.Done)
            {
                _continuousAgent.OnEpisodeEnd();
                CompleteEpisode();
                _continuousEnvironment.Reset();
            }
        }
        else if (_multiAgentEnvironment is not null && _learners is not null)
        {
            _learners.SetProgress(progress);

            var actions = _learners.SelectActions(_multiAgentEnvironment);
            LastAction = actions[0];

            var step = _multiAgentEnvironment.Step(actions);
            _learners.Observe(_multiAgentEnvironment, step);

            float total = 0f;
            foreach (float reward in _multiAgentEnvironment.LastRewards) total += reward;
            Accumulate(total);

            if (step.Done)
            {
                _learners.OnEpisodeEnd();
                CompleteEpisode();
                _multiAgentEnvironment.Reset();
            }
        }
    }

    private void Accumulate(float reward)
    {
        _episodeReturn += reward;
        _episodeSteps++;
    }

    private void CompleteEpisode()
    {
        Episode++;

        ReturnTrace.Add(_episodeReturn);
        BestReturn = float.IsNaN(BestReturn) ? _episodeReturn : MathF.Max(BestReturn, _episodeReturn);

        int window = Math.Min(20, ReturnTrace.Count);
        float sum = 0f;
        for (int i = ReturnTrace.Count - window; i < ReturnTrace.Count; i++) sum += ReturnTrace[i];
        RecentAverage = window > 0 ? sum / window : float.NaN;

        var metrics = Metrics;
        if (float.IsFinite(metrics.ValueLoss)) LossTrace.Add(metrics.ValueLoss);

        // Value-based agents explore by epsilon; policy-gradient agents explore through the
        // entropy of their own distribution. Both answer "how much is this agent still trying
        // things", so they share the trace and the legend says which is being shown.
        if (float.IsFinite(metrics.Epsilon))
        {
            ExplorationTrace.Add(metrics.Epsilon);
            ExplorationLabel = "EPSILON";
        }
        else if (float.IsFinite(metrics.Entropy))
        {
            ExplorationTrace.Add(metrics.Entropy);
            ExplorationLabel = "POLICY ENTROPY";
        }

        _episodeReturn = 0f;
        _episodeSteps = 0;
    }
}
