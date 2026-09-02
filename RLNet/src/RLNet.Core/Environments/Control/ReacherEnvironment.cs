// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Spaces;

namespace RLNet.Environments.Control;

/// <summary>
/// Drive a two-joint planar arm so its fingertip reaches a target that moves each episode.
/// </summary>
/// <remarks>
/// <para>
/// The robotics task of the set, modelled on MuJoCo's <c>Reacher-v4</c>. Full MuJoCo would mean
/// a native dependency and a licence; the arm here is a torque-driven double pendulum with
/// damping, which reproduces what makes the task instructive — redundant kinematics, coupled
/// joints, a continuous action space — without any of that weight. Scores are not comparable
/// with published MuJoCo numbers.
/// </para>
/// <para>
/// Its real teaching value is redundancy: most targets are reachable in two distinct joint
/// configurations, so the value function is genuinely multi-modal and a policy that averages
/// over both solutions reaches neither. Watching SAC commit to one of them is the clearest
/// picture in the library of why a stochastic policy with an entropy term behaves differently
/// from a deterministic one.
/// </para>
/// </remarks>
public sealed class ReacherEnvironment : ContinuousEnvironmentBase
{
    private const double LinkOne = 0.5;
    private const double LinkTwo = 0.5;
    private const double Dt = 0.05;
    private const double MaxTorque = 1.0;
    private const double MaxJointSpeed = 10.0;
    private const double Damping = 0.92;
    private const double Inertia = 0.1;

    /// <summary>Fingertip-to-target distance counted as a successful reach.</summary>
    public const double SuccessRadius = 0.05;

    private double _joint0, _joint1;
    private double _velocity0, _velocity1;
    private double _targetX, _targetY;

    public ReacherEnvironment()
        : base(
            new BoxSpace(
                [-1f, -1f, -1f, -1f, (float)-MaxJointSpeed, (float)-MaxJointSpeed, -1f, -1f],
                [1f, 1f, 1f, 1f, (float)MaxJointSpeed, (float)MaxJointSpeed, 1f, 1f],
                [
                    "cos(joint 0)", "sin(joint 0)", "cos(joint 1)", "sin(joint 1)",
                    "Joint 0 speed", "Joint 1 speed", "Target dx", "Target dy",
                ]),
            new BoxSpace(
                [(float)-MaxTorque, (float)-MaxTorque],
                [(float)MaxTorque, (float)MaxTorque],
                ["Shoulder torque", "Elbow torque"]),
            maxEpisodeSteps: 200)
    {
        Reset();
    }

    public override string Name => "Reacher";

    /// <summary>Shoulder angle, in radians.</summary>
    public double Joint0 => _joint0;

    /// <summary>Elbow angle relative to the first link, in radians.</summary>
    public double Joint1 => _joint1;

    /// <summary>Target x, in arm-length units.</summary>
    public double TargetX => _targetX;

    /// <summary>Target y, in arm-length units.</summary>
    public double TargetY => _targetY;

    /// <summary>Elbow position, for rendering the arm.</summary>
    public (double X, double Y) ElbowPosition =>
        (LinkOne * Math.Cos(_joint0), LinkOne * Math.Sin(_joint0));

    /// <summary>Fingertip position, the point being controlled.</summary>
    public (double X, double Y) FingertipPosition
    {
        get
        {
            double total = _joint0 + _joint1;
            return (LinkOne * Math.Cos(_joint0) + LinkTwo * Math.Cos(total),
                    LinkOne * Math.Sin(_joint0) + LinkTwo * Math.Sin(total));
        }
    }

    /// <summary>Current fingertip-to-target distance.</summary>
    public double DistanceToTarget
    {
        get
        {
            var (x, y) = FingertipPosition;
            return Math.Sqrt((x - _targetX) * (x - _targetX) + (y - _targetY) * (y - _targetY));
        }
    }

    protected override void OnReset()
    {
        _joint0 = Random.NextRange(-MathF.PI, MathF.PI);
        _joint1 = Random.NextRange(-MathF.PI, MathF.PI);
        _velocity0 = _velocity1 = 0.0;

        // Rejection-sample a target inside the annulus the arm can actually reach. Sampling the
        // square and clamping would pile targets onto the boundary and quietly change the task.
        double reach = LinkOne + LinkTwo;
        do
        {
            _targetX = Random.NextRange((float)-reach, (float)reach);
            _targetY = Random.NextRange((float)-reach, (float)reach);
        }
        while (Math.Sqrt(_targetX * _targetX + _targetY * _targetY) > reach * 0.95);
    }

    protected override void WriteObservation(Span<float> destination)
    {
        var (tipX, tipY) = FingertipPosition;

        // Joint angles go in as sine-cosine pairs for the same reason as in Pendulum: the raw
        // angle is periodic and a network cannot see that.
        destination[0] = (float)Math.Cos(_joint0);
        destination[1] = (float)Math.Sin(_joint0);
        destination[2] = (float)Math.Cos(_joint1);
        destination[3] = (float)Math.Sin(_joint1);
        destination[4] = (float)_velocity0;
        destination[5] = (float)_velocity1;

        // The target enters as a vector from the fingertip, not as an absolute position. That
        // makes the observation say "which way to move", which is the quantity the policy needs,
        // and it generalises across targets instead of memorising them.
        destination[6] = (float)(_targetX - tipX);
        destination[7] = (float)(_targetY - tipY);
    }

    protected override StepResult OnStep(ReadOnlySpan<float> action)
    {
        _velocity0 = Math.Clamp((_velocity0 + action[0] / Inertia * Dt) * Damping, -MaxJointSpeed, MaxJointSpeed);
        _velocity1 = Math.Clamp((_velocity1 + action[1] / Inertia * Dt) * Damping, -MaxJointSpeed, MaxJointSpeed);

        _joint0 += _velocity0 * Dt;
        _joint1 += _velocity1 * Dt;

        double distance = DistanceToTarget;

        // Distance dominates; the control cost is small but non-zero, which is what stops the
        // arm from thrashing around the target instead of settling on it.
        float reward = (float)(-distance - 0.01 * (action[0] * action[0] + action[1] * action[1]));

        bool terminated = distance < SuccessRadius;
        if (terminated) reward += 10f;

        return Advance(reward, terminated);
    }
}
