// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Spaces;
using RLNet.Utils;

namespace RLNet.Agents;

/// <summary>Tunable settings for <see cref="QTableAgent"/>.</summary>
public sealed class QTableOptions
{
    /// <summary>Step size for the tabular update.</summary>
    public float LearningRate { get; set; } = 0.1f;

    /// <summary>Discount factor.</summary>
    public float Gamma { get; set; } = 0.99f;

    /// <summary>Exploration schedule over training progress.</summary>
    public Schedule Epsilon { get; set; } = Schedule.Exponential(1f, 0.05f, 0.6f);

    /// <summary>
    /// Value new table entries start at.
    /// </summary>
    /// <remarks>
    /// Setting this above any achievable return is <i>optimistic initialisation</i>: every
    /// unvisited action looks better than every visited one, so the agent explores systematically
    /// rather than by chance. On small deterministic problems it outperforms epsilon-greedy
    /// outright. It defaults to 0, since on a stochastic environment it merely slows things down.
    /// </remarks>
    public float InitialValue { get; set; }
}

/// <summary>
/// Tabular Q-learning: one stored value per state-action pair, no function approximation.
/// </summary>
/// <remarks>
/// <para>
/// Watkins' original algorithm (1989), and still the right tool for a small discrete problem —
/// it converges to the optimal policy with probability 1 under mild conditions, which none of the
/// neural agents can claim. On GridWorld it finds the optimal path in a few hundred episodes,
/// far faster than DQN on the same task.
/// </para>
/// <para>
/// Its limit is not speed but memory: the table has an entry per distinct state, so it cannot
/// generalise across similar states and cannot be applied to a continuous space without
/// bucketing one first. That is precisely the wall DQN exists to get past, which is why keeping
/// both in the library is worth the duplication — running them side by side on GridWorld and
/// then on CartPole makes the trade-off concrete in a way no explanation does.
/// </para>
/// </remarks>
public sealed class QTableAgent : IDiscreteAgent
{
    private readonly Dictionary<long, float[]> _table = [];
    private readonly StateKeyEncoder _encoder;
    private readonly QTableOptions _options;
    private readonly FastRandom _random;
    private readonly int _actionCount;

    private float _progress;

    public QTableAgent(
        DiscreteSpace actionSpace,
        StateKeyEncoder encoder,
        QTableOptions? options = null,
        int? seed = null)
    {
        _actionCount = actionSpace.Count;
        _encoder = encoder;
        _options = options ?? new QTableOptions();
        _random = seed.HasValue ? new FastRandom(seed.Value) : new FastRandom();
    }

    public string Name => "Q-Learning";
    public AgentMetrics Metrics { get; } = new();

    /// <summary>Number of distinct states the agent has encountered.</summary>
    public int StateCount => _table.Count;

    public void SetProgress(float progress) => _progress = Math.Clamp(progress, 0f, 1f);

    public int SelectAction(ReadOnlySpan<float> observation, bool deterministic = false)
    {
        float epsilon = deterministic ? 0f : _options.Epsilon.At(_progress);
        Metrics.Epsilon = epsilon;

        if (epsilon > 0f && _random.NextSingle() < epsilon)
            return _random.NextInt(_actionCount);

        var values = ValuesFor(_encoder(observation));

        // Ties are broken at random rather than by taking the first maximum. An unvisited state
        // has all-equal values, and always picking action 0 there would make the agent's early
        // behaviour a systematic bias instead of a uniform prior.
        float best = SimdOps.Max(values);
        int tieCount = 0;
        for (int a = 0; a < _actionCount; a++)
            if (values[a] >= best - 1e-6f) tieCount++;

        if (tieCount == 1) { SimdOps.Max(values, out int single); return single; }

        int choice = _random.NextInt(tieCount);
        for (int a = 0; a < _actionCount; a++)
            if (values[a] >= best - 1e-6f && choice-- == 0) return a;

        return _actionCount - 1;
    }

    public void Observe(
        ReadOnlySpan<float> observation,
        int action,
        float reward,
        ReadOnlySpan<float> nextObservation,
        bool terminated,
        bool truncated)
    {
        var current = ValuesFor(_encoder(observation));

        // Only true termination zeroes the bootstrap. An episode cut off by the step limit has a
        // future the agent should still account for.
        float bootstrap = terminated ? 0f : SimdOps.Max(ValuesFor(_encoder(nextObservation)));

        float target = reward + _options.Gamma * bootstrap;
        float error = target - current[action];

        current[action] += _options.LearningRate * error;

        Metrics.TdError = MathF.Abs(error);
        Metrics.ValueLoss = 0.5f * error * error;
        Metrics.StepCount++;
        Metrics.UpdateCount++;
    }

    public void OnEpisodeEnd() { }

    private float[] ValuesFor(long key)
    {
        // CollectionsMarshal-free by design: the table is not on the same hot path as the neural
        // agents, and the straightforward version is far easier to reason about.
        if (_table.TryGetValue(key, out var values)) return values;

        values = new float[_actionCount];
        if (_options.InitialValue != 0f) values.AsSpan().Fill(_options.InitialValue);

        _table[key] = values;
        return values;
    }

    /// <summary>Reads the learned action values for a state, for rendering a value map.</summary>
    public ReadOnlySpan<float> ValuesForObservation(ReadOnlySpan<float> observation) =>
        _table.TryGetValue(_encoder(observation), out var values) ? values : default;

    /// <summary>Empties the table.</summary>
    public void Clear() => _table.Clear();
}
