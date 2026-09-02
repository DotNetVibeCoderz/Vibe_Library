// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Spaces;

namespace RLNet.Environments.Classic;

/// <summary>
/// Land a spacecraft on a pad between two flags using a main engine and two attitude thrusters.
/// </summary>
/// <remarks>
/// <para>
/// Gymnasium's LunarLander is a Box2D rigid-body simulation. This is a lighter analytic model —
/// point mass, torque-driven attitude, no contact solver — which keeps the library free of a
/// native physics dependency. The observation layout, the action set and the shape of the reward
/// follow the original, so an agent written for one transfers to the other, but the absolute
/// scores are <b>not</b> comparable with published LunarLander-v2 numbers.
/// </para>
/// <para>
/// The reward is shaped rather than sparse: most of it comes from a potential over distance,
/// speed and tilt, so the agent gets a gradient long before it ever lands. Landing pays +100,
/// crashing costs -100, and firing an engine costs a little fuel every step — which is what
/// stops the agent from hovering forever once it discovers that not crashing pays.
/// </para>
/// </remarks>
public sealed class LunarLanderEnvironment : DiscreteEnvironmentBase
{
    private const double Gravity = -0.0015;
    private const double MainEngineThrust = 0.0035;
    private const double SideEngineThrust = 0.0008;
    private const double SideEngineTorque = 0.0075;
    private const double AngularDamping = 0.985;

    /// <summary>Half-width of the landing pad, centred on the origin.</summary>
    public const double PadHalfWidth = 0.2;

    private double _x, _y, _vx, _vy, _angle, _angularVelocity;
    private double _previousShaping;
    private bool _landed;

    public LunarLanderEnvironment()
        : base(
            new BoxSpace(
                [-1.5f, -0.5f, -2f, -2f, -3.14f, -5f],
                [1.5f, 1.8f, 2f, 2f, 3.14f, 5f],
                ["X", "Altitude", "X velocity", "Y velocity", "Tilt", "Spin"]),
            new DiscreteSpace(4, ["Coast", "Main engine", "Left thruster", "Right thruster"]),
            maxEpisodeSteps: 1000)
    {
        Reset();
    }

    public override string Name => "LunarLander";

    /// <summary>Horizontal position; the pad is centred on 0.</summary>
    public double X => _x;

    /// <summary>Altitude above the surface.</summary>
    public double Y => _y;

    /// <summary>Tilt from upright, in radians.</summary>
    public double Angle => _angle;

    /// <summary>Whether the main engine fired on the last step, for rendering the plume.</summary>
    public bool MainEngineFiring { get; private set; }

    /// <summary>Whether the left thruster fired on the last step.</summary>
    public bool LeftThrusterFiring { get; private set; }

    /// <summary>Whether the right thruster fired on the last step.</summary>
    public bool RightThrusterFiring { get; private set; }

    /// <summary>Whether the last episode ended on the pad rather than in a crash.</summary>
    public bool Landed => _landed;

    protected override void OnReset()
    {
        _x = Random.NextRange(-0.4f, 0.4f);
        _y = 1.4;
        _vx = Random.NextRange(-0.02f, 0.02f);
        _vy = 0.0;
        _angle = Random.NextRange(-0.1f, 0.1f);
        _angularVelocity = 0.0;
        _landed = false;
        MainEngineFiring = LeftThrusterFiring = RightThrusterFiring = false;

        _previousShaping = Shaping();
    }

    protected override void WriteObservation(Span<float> destination)
    {
        destination[0] = (float)_x;
        destination[1] = (float)_y;
        destination[2] = (float)_vx;
        destination[3] = (float)_vy;
        destination[4] = (float)_angle;
        destination[5] = (float)_angularVelocity;
    }

    /// <summary>
    /// Potential function over the lander's state: higher is closer to a good landing.
    /// </summary>
    /// <remarks>
    /// Rewarding the <em>change</em> in this rather than its value is potential-based shaping
    /// (Ng, Harada and Russell, 1999), and the reason that matters is that it provably leaves
    /// the optimal policy unchanged. Rewarding the value directly would pay the agent to loiter
    /// near the pad, which is a different task from landing on it.
    /// </remarks>
    private double Shaping() =>
        -100.0 * Math.Sqrt(_x * _x + _y * _y)
        - 100.0 * Math.Sqrt(_vx * _vx + _vy * _vy)
        - 100.0 * Math.Abs(_angle);

    protected override StepResult OnStep(int action)
    {
        MainEngineFiring = action == 1;
        LeftThrusterFiring = action == 2;
        RightThrusterFiring = action == 3;

        double thrustX = 0.0, thrustY = 0.0;
        double fuelCost = 0.0;

        if (MainEngineFiring)
        {
            // Thrust is along the lander's own axis, so tilt steers as well as it wastes.
            thrustX = -Math.Sin(_angle) * MainEngineThrust;
            thrustY = Math.Cos(_angle) * MainEngineThrust;
            fuelCost = 0.30;
        }
        else if (LeftThrusterFiring)
        {
            thrustX = SideEngineThrust;
            _angularVelocity += SideEngineTorque;
            fuelCost = 0.03;
        }
        else if (RightThrusterFiring)
        {
            thrustX = -SideEngineThrust;
            _angularVelocity -= SideEngineTorque;
            fuelCost = 0.03;
        }

        _vx += thrustX;
        _vy += thrustY + Gravity;
        _x += _vx;
        _y += _vy;

        _angularVelocity *= AngularDamping;
        _angle += _angularVelocity;

        double shaping = Shaping();
        float reward = (float)(shaping - _previousShaping - fuelCost);
        _previousShaping = shaping;

        bool terminated = false;

        if (_y <= 0.0)
        {
            _y = 0.0;
            terminated = true;

            // Touching down is not the same as landing: it counts only on the pad, upright, and
            // slowly enough to survive. Everything else is a crash.
            _landed =
                Math.Abs(_x) <= PadHalfWidth &&
                Math.Abs(_angle) < 0.25 &&
                Math.Abs(_vy) < 0.06 &&
                Math.Abs(_vx) < 0.06;

            reward += _landed ? 100f : -100f;
        }
        else if (Math.Abs(_x) > 1.5 || _y > 1.8)
        {
            // Flying off the map is a failure, but a less instructive one than a crash, so it
            // costs less. Penalising it as heavily as a crash makes the agent afraid of altitude.
            terminated = true;
            reward -= 50f;
        }

        return Advance(reward, terminated);
    }
}
