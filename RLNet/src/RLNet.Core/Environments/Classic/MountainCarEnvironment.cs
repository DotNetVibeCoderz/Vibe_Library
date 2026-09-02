// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Spaces;

namespace RLNet.Environments.Classic;

/// <summary>
/// Drive an underpowered car out of a valley by rocking it up the opposite slope first.
/// </summary>
/// <remarks>
/// <para>
/// Constants match Gymnasium's <c>MountainCar-v0</c>. The engine cannot beat gravity directly,
/// so the only solution is to build momentum by accelerating <em>away</em> from the goal — which
/// makes this the exploration benchmark of the set. Reward is -1 per step until the flag, so
/// until the very first success every policy scores exactly -200 and the gradient carries no
/// signal at all.
/// </para>
/// <para>
/// That property is the reason it is here. Epsilon-greedy DQN often never solves it; the same
/// agent with prioritised replay usually does, and the difference is visible in a single
/// training curve. It is the cheapest demonstration in the library of why exploration is a
/// separate problem from optimisation.
/// </para>
/// </remarks>
public sealed class MountainCarEnvironment : DiscreteEnvironmentBase
{
    private const double MinPosition = -1.2;
    private const double MaxPosition = 0.6;
    private const double MaxSpeed = 0.07;
    private const double GoalPosition = 0.5;
    private const double GoalVelocity = 0.0;
    private const double Force = 0.001;
    private const double Gravity = 0.0025;

    private double _position, _velocity;

    public MountainCarEnvironment()
        : base(
            new BoxSpace(
                [(float)MinPosition, (float)-MaxSpeed],
                [(float)MaxPosition, (float)MaxSpeed],
                ["Position", "Velocity"]),
            new DiscreteSpace(3, ["Accelerate left", "Coast", "Accelerate right"]),
            maxEpisodeSteps: 200)
    {
        Reset();
    }

    public override string Name => "MountainCar";

    /// <summary>Car position along the valley.</summary>
    public double Position => _position;

    /// <summary>Car velocity, signed.</summary>
    public double Velocity => _velocity;

    /// <summary>Height of the track at a position, for rendering the hill.</summary>
    public static double TrackHeight(double position) => Math.Sin(3.0 * position) * 0.45 + 0.55;

    protected override void OnReset()
    {
        _position = Random.NextRange(-0.6f, -0.4f);
        _velocity = 0.0;
    }

    protected override void WriteObservation(Span<float> destination)
    {
        destination[0] = (float)_position;
        destination[1] = (float)_velocity;
    }

    protected override StepResult OnStep(int action)
    {
        // Actions map to -1, 0, +1. Engine force is an order of magnitude below the gravity
        // term at the steepest point, which is precisely why the direct approach fails.
        _velocity += (action - 1) * Force + Math.Cos(3.0 * _position) * -Gravity;
        _velocity = Math.Clamp(_velocity, -MaxSpeed, MaxSpeed);

        _position += _velocity;
        _position = Math.Clamp(_position, MinPosition, MaxPosition);

        // The left edge is a wall, not a cliff: hitting it kills the velocity rather than
        // reflecting it, so momentum built on that side has to be earned again.
        if (_position <= MinPosition && _velocity < 0.0) _velocity = 0.0;

        bool terminated = _position >= GoalPosition && _velocity >= GoalVelocity;

        return Advance(-1f, terminated);
    }
}
