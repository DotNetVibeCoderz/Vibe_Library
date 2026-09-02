// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Spaces;
using RLNet.Utils;

namespace RLNet.Environments.MultiAgent;

/// <summary>
/// Several predators cooperate on a toroidal grid to corner a fleeing prey.
/// </summary>
/// <remarks>
/// <para>
/// The classic pursuit task, chosen because it cannot be solved by an agent acting alone.
/// Capture requires two predators on the prey's cell <em>at the same time</em>, so a predator
/// that only chases is a predator that never scores — the reward is shared, and the behaviour
/// that earns it has to be coordinated. That is the whole point of putting it in the library.
/// </para>
/// <para>
/// The grid wraps, which removes corners. On a bounded grid predators learn to herd the prey
/// into a corner and the task collapses into a much easier one; on a torus there is nowhere to
/// pin it and real encirclement is the only strategy that works.
/// </para>
/// <para>
/// Each predator sees only a local window around itself plus the wrapped direction to the prey,
/// not the full board. Partial observability is what makes the coordination problem non-trivial:
/// with global state, each predator could just compute the joint plan itself.
/// </para>
/// </remarks>
public sealed class PredatorPreyEnvironment : IMultiAgentEnvironment
{
    private const int ViewRadius = 2;

    private readonly int _size;
    private readonly int _predatorCount;
    private readonly FastRandom _random = new();

    private readonly int[] _predatorX;
    private readonly int[] _predatorY;
    private readonly float[] _rewards;
    private readonly float[][] _observations;

    private int _preyX, _preyY;
    private int _captures;

    /// <param name="gridSize">Side length of the square torus.</param>
    /// <param name="predatorCount">Number of learning agents. Two are needed for any capture.</param>
    public PredatorPreyEnvironment(int gridSize = 9, int predatorCount = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(predatorCount, 2);

        _size = gridSize;
        _predatorCount = predatorCount;
        _predatorX = new int[predatorCount];
        _predatorY = new int[predatorCount];
        _rewards = new float[predatorCount];

        int window = ViewRadius * 2 + 1;

        // Two occupancy planes over the local window (other predators, prey) plus a wrapped
        // bearing to the prey and a normalised distance.
        int observationSize = window * window * 2 + 3;

        ObservationSpace = BoxSpace.Uniform(observationSize, -1f, 1f);
        ActionSpace = new DiscreteSpace(5, ["Stay", "Up", "Down", "Left", "Right"]);

        _observations = new float[predatorCount][];
        for (int i = 0; i < predatorCount; i++)
            _observations[i] = new float[observationSize];

        Reset();
    }

    public string Name => "PredatorPrey";
    public int AgentCount => _predatorCount;
    public Space ObservationSpace { get; }
    public DiscreteSpace ActionSpace { get; }
    public int ElapsedSteps { get; private set; }
    public int MaxEpisodeSteps => 200;

    /// <summary>Side length of the grid.</summary>
    public int GridSize => _size;

    /// <summary>Position of one predator.</summary>
    public (int X, int Y) PredatorAt(int index) => (_predatorX[index], _predatorY[index]);

    /// <summary>Position of the prey.</summary>
    public (int X, int Y) Prey => (_preyX, _preyY);

    /// <summary>Captures achieved this episode.</summary>
    public int Captures => _captures;

    public ReadOnlySpan<float> ObservationOf(int agent) => _observations[agent];
    public ReadOnlySpan<float> LastRewards => _rewards;
    public string AgentName(int agent) => $"Predator {agent + 1}";

    public void Reset(int? seed = null)
    {
        if (seed.HasValue) _random.Seed(seed.Value);

        ElapsedSteps = 0;
        _captures = 0;

        _preyX = _random.NextInt(_size);
        _preyY = _random.NextInt(_size);

        for (int i = 0; i < _predatorCount; i++)
        {
            // Start predators away from the prey, so an episode cannot be won by accident on
            // step one and the learning signal comes from actual pursuit.
            do
            {
                _predatorX[i] = _random.NextInt(_size);
                _predatorY[i] = _random.NextInt(_size);
            }
            while (WrappedDistance(_predatorX[i], _predatorY[i], _preyX, _preyY) < 3);
        }

        Array.Clear(_rewards);
        WriteObservations();
    }

    public MultiAgentStepResult Step(ReadOnlySpan<int> actions)
    {
        if (actions.Length != _predatorCount)
            throw new ArgumentException($"Expected {_predatorCount} actions, got {actions.Length}.", nameof(actions));

        Array.Clear(_rewards);

        for (int i = 0; i < _predatorCount; i++)
        {
            var (dx, dy) = Delta(actions[i]);
            _predatorX[i] = Wrap(_predatorX[i] + dx);
            _predatorY[i] = Wrap(_predatorY[i] + dy);

            // A small step cost, so dithering is never free and pursuit has to be purposeful.
            _rewards[i] -= 0.05f;
        }

        MovePrey();

        int onPrey = 0;
        for (int i = 0; i < _predatorCount; i++)
            if (_predatorX[i] == _preyX && _predatorY[i] == _preyY) onPrey++;

        bool captured = onPrey >= 2;
        if (captured)
        {
            _captures++;

            // The capture reward goes to every predator, not just the ones standing on the prey.
            // Paying only the occupiers rewards the last one to arrive and teaches the others
            // nothing about the manoeuvre that set it up.
            for (int i = 0; i < _predatorCount; i++) _rewards[i] += 10f;

            // The prey respawns rather than ending the episode, so one episode can teach several
            // captures and the return distinguishes a lucky predator from a consistent one.
            RespawnPrey();
        }
        else if (onPrey == 1)
        {
            // Reaching the prey alone is progress, and worth a nudge — but only a small one, or
            // predators learn to sit on the prey and wait instead of coordinating.
            for (int i = 0; i < _predatorCount; i++)
                if (_predatorX[i] == _preyX && _predatorY[i] == _preyY) _rewards[i] += 0.5f;
        }

        ElapsedSteps++;
        WriteObservations();

        // There is no terminal state: the episode always runs to the step limit, so return is
        // "captures per episode" and is directly comparable between runs.
        return new MultiAgentStepResult(Terminated: false, Truncated: ElapsedSteps >= MaxEpisodeSteps);
    }

    private void MovePrey()
    {
        // The prey is a fixed heuristic, not a learner: it flees the nearest predator. That keeps
        // the training signal stationary enough to be readable, while still being evasive enough
        // that a single predator can never corner it on a torus.
        int nearest = 0;
        int best = int.MaxValue;
        for (int i = 0; i < _predatorCount; i++)
        {
            int d = WrappedDistance(_predatorX[i], _predatorY[i], _preyX, _preyY);
            if (d < best)
            {
                best = d;
                nearest = i;
            }
        }

        // Occasional random moves stop the predators from learning a purely reactive counter to
        // a deterministic prey.
        if (_random.NextSingle() < 0.2f)
        {
            var (rdx, rdy) = Delta(_random.NextInt(5));
            _preyX = Wrap(_preyX + rdx);
            _preyY = Wrap(_preyY + rdy);
            return;
        }

        int fleeX = -Math.Sign(WrappedDelta(_predatorX[nearest], _preyX));
        int fleeY = -Math.Sign(WrappedDelta(_predatorY[nearest], _preyY));

        // Move on one axis per step, matching the predators' movement budget.
        if (_random.NextSingle() < 0.5f && fleeX != 0) _preyX = Wrap(_preyX + fleeX);
        else if (fleeY != 0) _preyY = Wrap(_preyY + fleeY);
        else if (fleeX != 0) _preyX = Wrap(_preyX + fleeX);
    }

    private void RespawnPrey()
    {
        do
        {
            _preyX = _random.NextInt(_size);
            _preyY = _random.NextInt(_size);
        }
        while (MinimumPredatorDistance() < 3);
    }

    private int MinimumPredatorDistance()
    {
        int best = int.MaxValue;
        for (int i = 0; i < _predatorCount; i++)
            best = Math.Min(best, WrappedDistance(_predatorX[i], _predatorY[i], _preyX, _preyY));
        return best;
    }

    private void WriteObservations()
    {
        int window = ViewRadius * 2 + 1;
        int planeSize = window * window;

        for (int agent = 0; agent < _predatorCount; agent++)
        {
            var view = _observations[agent].AsSpan();
            view.Clear();

            int cx = _predatorX[agent], cy = _predatorY[agent];

            for (int dy = -ViewRadius; dy <= ViewRadius; dy++)
            {
                for (int dx = -ViewRadius; dx <= ViewRadius; dx++)
                {
                    int wx = Wrap(cx + dx), wy = Wrap(cy + dy);
                    int cell = (dy + ViewRadius) * window + (dx + ViewRadius);

                    for (int other = 0; other < _predatorCount; other++)
                        if (other != agent && _predatorX[other] == wx && _predatorY[other] == wy)
                            view[cell] = 1f;

                    if (_preyX == wx && _preyY == wy)
                        view[planeSize + cell] = 1f;
                }
            }

            // A bearing to the prey on top of the local window. Without it a predator that has
            // lost sight of the prey has no information at all and wanders, which makes the
            // credit assignment far too sparse to learn from in a 200-step episode.
            int deltaX = WrappedDelta(_preyX, cx);
            int deltaY = WrappedDelta(_preyY, cy);
            view[planeSize * 2] = deltaX / (float)(_size / 2);
            view[planeSize * 2 + 1] = deltaY / (float)(_size / 2);
            view[planeSize * 2 + 2] = WrappedDistance(cx, cy, _preyX, _preyY) / (float)_size;
        }
    }

    private static (int X, int Y) Delta(int action) => action switch
    {
        1 => (0, -1),
        2 => (0, 1),
        3 => (-1, 0),
        4 => (1, 0),
        _ => (0, 0),
    };

    private int Wrap(int value) => ((value % _size) + _size) % _size;

    /// <summary>Signed shortest offset from <paramref name="from"/> to <paramref name="to"/> around the wrap.</summary>
    private int WrappedDelta(int to, int from)
    {
        int direct = to - from;
        if (direct > _size / 2) direct -= _size;
        else if (direct < -_size / 2) direct += _size;
        return direct;
    }

    /// <summary>Manhattan distance on the torus.</summary>
    private int WrappedDistance(int x1, int y1, int x2, int y2) =>
        Math.Abs(WrappedDelta(x2, x1)) + Math.Abs(WrappedDelta(y2, y1));
}
