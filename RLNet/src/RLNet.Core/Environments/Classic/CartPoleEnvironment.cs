// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Environments;
using RLNet.Spaces;

namespace RLNet.Environments.Classic;

/// <summary>
/// Balance a pole hinged to a cart by pushing the cart left or right.
/// </summary>
/// <remarks>
/// <para>
/// The reference benchmark for discrete control, after Barto, Sutton and Anderson (1983). The
/// constants and termination bounds match Gymnasium's <c>CartPole-v1</c> exactly, so a score
/// reported here means the same thing as a score reported by Stable-Baselines3: 500 is a perfect
/// episode and anything above 475 counts as solved.
/// </para>
/// <para>
/// Worth knowing when reading a training curve: the reward is +1 per surviving step, so return
/// and episode length are the same number. A rising curve is the pole staying up longer, nothing
/// more subtle than that.
/// </para>
/// </remarks>
public sealed class CartPoleEnvironment : DiscreteEnvironmentBase
{
    private const double Gravity = 9.8;
    private const double CartMass = 1.0;
    private const double PoleMass = 0.1;
    private const double TotalMass = CartMass + PoleMass;
    private const double HalfPoleLength = 0.5;
    private const double PoleMassLength = PoleMass * HalfPoleLength;
    private const double ForceMagnitude = 10.0;
    private const double Tau = 0.02; // seconds per step

    /// <summary>Pole angle beyond which the episode ends, 12 degrees in radians.</summary>
    public const double AngleThreshold = 12.0 * 2.0 * Math.PI / 360.0;

    /// <summary>Cart displacement beyond which the episode ends.</summary>
    public const double PositionThreshold = 2.4;

    // Physics is integrated in double precision even though observations are float. The
    // integrator compounds error over 500 steps, and matching the reference implementation's
    // trajectories matters more here than the few bytes saved.
    private double _x, _xDot, _theta, _thetaDot;

    public CartPoleEnvironment()
        : base(
            new BoxSpace(
                [-4.8f, float.NegativeInfinity, (float)(-AngleThreshold * 2), float.NegativeInfinity],
                [4.8f, float.PositiveInfinity, (float)(AngleThreshold * 2), float.PositiveInfinity],
                ["Cart position", "Cart velocity", "Pole angle", "Pole angular velocity"]),
            new DiscreteSpace(2, ["Push left", "Push right"]),
            maxEpisodeSteps: 500)
    {
        Reset();
    }

    public override string Name => "CartPole";

    /// <summary>Cart position along the track, in metres from centre.</summary>
    public double CartPosition => _x;

    /// <summary>Pole angle from vertical, in radians.</summary>
    public double PoleAngle => _theta;

    protected override void OnReset()
    {
        _x = Random.NextRange(-0.05f, 0.05f);
        _xDot = Random.NextRange(-0.05f, 0.05f);
        _theta = Random.NextRange(-0.05f, 0.05f);
        _thetaDot = Random.NextRange(-0.05f, 0.05f);
    }

    protected override void WriteObservation(Span<float> destination)
    {
        destination[0] = (float)_x;
        destination[1] = (float)_xDot;
        destination[2] = (float)_theta;
        destination[3] = (float)_thetaDot;
    }

    protected override StepResult OnStep(int action)
    {
        double force = action == 1 ? ForceMagnitude : -ForceMagnitude;
        double cosTheta = Math.Cos(_theta);
        double sinTheta = Math.Sin(_theta);

        // Standard cart-pole dynamics: solve the coupled equations for angular acceleration
        // first, then substitute back for the cart's linear acceleration.
        double temp = (force + PoleMassLength * _thetaDot * _thetaDot * sinTheta) / TotalMass;
        double thetaAcceleration =
            (Gravity * sinTheta - cosTheta * temp) /
            (HalfPoleLength * (4.0 / 3.0 - PoleMass * cosTheta * cosTheta / TotalMass));
        double xAcceleration = temp - PoleMassLength * thetaAcceleration * cosTheta / TotalMass;

        // Semi-implicit Euler: velocity is updated first and the new velocity drives the
        // position. Matches the reference implementation, and is noticeably more stable than
        // explicit Euler at this step size.
        _xDot += Tau * xAcceleration;
        _x += Tau * _xDot;
        _thetaDot += Tau * thetaAcceleration;
        _theta += Tau * _thetaDot;

        bool terminated =
            _x < -PositionThreshold || _x > PositionThreshold ||
            _theta < -AngleThreshold || _theta > AngleThreshold;

        // A flat +1 for every step survived, including the one that fails. The agent's only
        // lever is how many steps it gets, which is what makes the return so easy to read.
        return Advance(1f, terminated);
    }
}
