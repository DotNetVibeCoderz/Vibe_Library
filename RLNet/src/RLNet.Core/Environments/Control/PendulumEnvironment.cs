// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Spaces;

namespace RLNet.Environments.Control;

/// <summary>
/// Swing a pendulum upright and hold it there with a torque too weak to lift it directly.
/// </summary>
/// <remarks>
/// <para>
/// Constants match Gymnasium's <c>Pendulum-v1</c>, and this is the reference task for the
/// continuous-action agents: SAC and TD3 both solve it in a few tens of thousands of steps,
/// while no discrete agent can express the fine torque control it needs.
/// </para>
/// <para>
/// The observation is <c>[cos θ, sin θ, θ̇]</c> rather than <c>[θ, θ̇]</c>, and that is not
/// decoration. The angle is periodic, so θ = -π and θ = π are the same state but the furthest
/// apart two numbers can be; a network fed the raw angle has to learn to glue the ends together
/// and mostly fails. The sine-cosine pair makes the topology of the circle explicit.
/// </para>
/// <para>
/// Reward is always negative — it is a cost — so a good policy approaches 0 from below.
/// About -150 per episode is solved; a policy that never swings up scores around -1200.
/// </para>
/// </remarks>
public sealed class PendulumEnvironment : ContinuousEnvironmentBase
{
    private const double MaxSpeed = 8.0;
    private const double MaxTorque = 2.0;
    private const double Dt = 0.05;
    private const double Gravity = 10.0;
    private const double Mass = 1.0;
    private const double Length = 1.0;

    private double _theta;        // 0 is upright
    private double _thetaDot;
    private double _lastTorque;

    public PendulumEnvironment()
        : base(
            new BoxSpace(
                [-1f, -1f, (float)-MaxSpeed],
                [1f, 1f, (float)MaxSpeed],
                ["cos(angle)", "sin(angle)", "Angular velocity"]),
            new BoxSpace([(float)-MaxTorque], [(float)MaxTorque], ["Torque"]),
            maxEpisodeSteps: 200)
    {
        Reset();
    }

    public override string Name => "Pendulum";

    /// <summary>Angle from upright, in radians.</summary>
    public double Angle => _theta;

    /// <summary>Angular velocity.</summary>
    public double AngularVelocity => _thetaDot;

    /// <summary>Torque applied on the last step, for rendering the effort arc.</summary>
    public double LastTorque => _lastTorque;

    protected override void OnReset()
    {
        // Uniform over the whole circle, so roughly half of all episodes start below horizontal
        // and require the swing-up rather than just a correction.
        _theta = Random.NextRange(-MathF.PI, MathF.PI);
        _thetaDot = Random.NextRange(-1f, 1f);
        _lastTorque = 0.0;
    }

    protected override void WriteObservation(Span<float> destination)
    {
        destination[0] = (float)Math.Cos(_theta);
        destination[1] = (float)Math.Sin(_theta);
        destination[2] = (float)_thetaDot;
    }

    protected override StepResult OnStep(ReadOnlySpan<float> action)
    {
        double torque = Math.Clamp(action[0], -MaxTorque, MaxTorque);
        _lastTorque = torque;

        // The cost is evaluated on the state the agent acted in, before the step, so that the
        // torque penalty lands on the action that incurred it.
        double normalised = NormaliseAngle(_theta);
        double cost =
            normalised * normalised +
            0.1 * _thetaDot * _thetaDot +
            0.001 * torque * torque;

        double acceleration =
            3.0 * Gravity / (2.0 * Length) * Math.Sin(_theta) +
            3.0 / (Mass * Length * Length) * torque;

        _thetaDot = Math.Clamp(_thetaDot + acceleration * Dt, -MaxSpeed, MaxSpeed);
        _theta += _thetaDot * Dt;

        // Pendulum has no terminal state at all: the episode ends only on the step limit, which
        // makes it the environment where getting truncation bootstrapping right matters most.
        // Treating the cutoff as terminal here costs several hundred points of final return.
        return Advance((float)-cost, terminated: false);
    }

    /// <summary>Wraps an angle into <c>[-π, π]</c>.</summary>
    private static double NormaliseAngle(double angle)
    {
        double wrapped = (angle + Math.PI) % (2.0 * Math.PI);
        if (wrapped < 0) wrapped += 2.0 * Math.PI;
        return wrapped - Math.PI;
    }
}
