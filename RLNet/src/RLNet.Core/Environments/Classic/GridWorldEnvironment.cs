// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Spaces;

namespace RLNet.Environments.Classic;

/// <summary>What occupies one grid cell.</summary>
public enum GridCell
{
    Empty = 0,
    Wall = 1,
    Goal = 2,
    Trap = 3,
}

/// <summary>
/// Navigate a grid from the top-left corner to the goal without stepping in a trap.
/// </summary>
/// <remarks>
/// <para>
/// The smallest environment here, and the one to reach for first when something is wrong: its
/// optimal policy can be worked out by hand, so an agent that fails on GridWorld has a bug
/// rather than a hyper-parameter problem.
/// </para>
/// <para>
/// The observation is one-hot over cells rather than the raw <c>(x, y)</c> pair. Feeding
/// coordinates to a network implies that cell 4 is twice cell 2 in some meaningful sense, which
/// is false on a grid, and neural agents learn visibly worse from it. Tabular Q-learning is
/// unaffected either way, since it only ever compares observations for equality.
/// </para>
/// </remarks>
public sealed class GridWorldEnvironment : DiscreteEnvironmentBase
{
    private readonly GridCell[] _cells;
    private int _agentX, _agentY;

    public int Width { get; }
    public int Height { get; }

    /// <summary>Agent column, from the left.</summary>
    public int AgentX => _agentX;

    /// <summary>Agent row, from the top.</summary>
    public int AgentY => _agentY;

    /// <summary>Creates the default 5x5 layout with three traps between start and goal.</summary>
    public GridWorldEnvironment() : this(5, 5, DefaultLayout(5, 5)) { }

    /// <param name="cells">Row-major cell types, <c>width * height</c> long.</param>
    public GridWorldEnvironment(int width, int height, GridCell[] cells)
        : base(
            BoxSpace.Uniform(width * height, 0f, 1f),
            new DiscreteSpace(4, ["Up", "Down", "Left", "Right"]),
            // Generous enough that a competent policy is never cut off, tight enough that a
            // policy circling forever still yields the episode and lets learning continue.
            maxEpisodeSteps: width * height * 4)
    {
        if (cells.Length != width * height)
            throw new ArgumentException($"Expected {width * height} cells, got {cells.Length}.", nameof(cells));

        Width = width;
        Height = height;
        _cells = cells;
        Reset();
    }

    private static GridCell[] DefaultLayout(int width, int height)
    {
        var cells = new GridCell[width * height];
        cells[(height - 1) * width + (width - 1)] = GridCell.Goal;
        cells[1 * width + 1] = GridCell.Trap;
        cells[2 * width + 2] = GridCell.Trap;
        cells[1 * width + 3] = GridCell.Trap;
        return cells;
    }

    public override string Name => "GridWorld";

    /// <summary>Reads the cell type at a position, for rendering.</summary>
    public GridCell CellAt(int x, int y) => _cells[y * Width + x];

    protected override void OnReset()
    {
        _agentX = 0;
        _agentY = 0;
    }

    protected override void WriteObservation(Span<float> destination)
    {
        destination.Clear();
        destination[_agentY * Width + _agentX] = 1f;
    }

    protected override StepResult OnStep(int action)
    {
        int nextX = _agentX, nextY = _agentY;
        switch (action)
        {
            case 0: nextY--; break;
            case 1: nextY++; break;
            case 2: nextX--; break;
            case 3: nextX++; break;
        }

        // Walking into a boundary or a wall costs a step and a small penalty but does not end
        // the episode — the agent has to learn the shape of the room, not be rescued from it.
        bool blocked =
            nextX < 0 || nextX >= Width || nextY < 0 || nextY >= Height ||
            _cells[nextY * Width + nextX] == GridCell.Wall;

        if (blocked) return Advance(-1f, terminated: false);

        _agentX = nextX;
        _agentY = nextY;

        return _cells[nextY * Width + nextX] switch
        {
            GridCell.Goal => Advance(10f, terminated: true),
            GridCell.Trap => Advance(-10f, terminated: true),

            // A small cost per step is what turns "reach the goal" into "reach the goal quickly".
            // Without it every path that eventually arrives scores the same and the agent has no
            // reason to prefer the short one.
            _ => Advance(-0.1f, terminated: false),
        };
    }
}
