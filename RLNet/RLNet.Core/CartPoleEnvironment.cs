using System;

namespace RLNet.Core
{
    public class CartPoleEnvironment : IEnvironment
    {
        // Physics constants
        private const double Gravity = 9.8;
        private const double MassCart = 1.0;
        private const double MassPole = 0.1;
        private const double TotalMass = MassCart + MassPole;
        private const double Length = 0.5; // actually half the pole's length
        private const double PoleMassLength = MassPole * Length;
        private const double ForceMag = 10.0;
        private const double Tau = 0.02; // seconds between state updates

        // Thresholds
        private const double ThetaThresholdRadians = 12 * 2 * Math.PI / 360; // 12 degrees
        private const double XThreshold = 2.4;

        // State: [x, x_dot, theta, theta_dot]
        private double[] _state;
        private Random _random;
        private int _steps;

        public double CartX => _state[0];
        public double PoleAngle => _state[2];

        public CartPoleEnvironment()
        {
            _random = new Random();
            Reset();
        }

        public int GetActionSpaceSize() => 2; // 0: Left, 1: Right
        public int[] GetObservationSpaceShape() => new int[] { 4 };

        public StepResult Reset()
        {
            _steps = 0;
            // Random start between -0.05 and 0.05
            _state = new double[4];
            for (int i = 0; i < 4; i++) _state[i] = (_random.NextDouble() * 0.1) - 0.05;

            return new StepResult
            {
                State = (double[])_state.Clone(),
                Reward = 0,
                Done = false,
                Info = "Reset"
            };
        }

        public StepResult Step(int action)
        {
            double x = _state[0];
            double x_dot = _state[1];
            double theta = _state[2];
            double theta_dot = _state[3];

            double force = (action == 1) ? ForceMag : -ForceMag;

            double costheta = Math.Cos(theta);
            double sintheta = Math.Sin(theta);

            double temp = (force + PoleMassLength * theta_dot * theta_dot * sintheta) / TotalMass;
            double thetaacc = (Gravity * sintheta - costheta * temp) / (Length * (4.0 / 3.0 - MassPole * costheta * costheta / TotalMass));
            double xacc = temp - PoleMassLength * thetaacc * costheta / TotalMass;

            x = x + Tau * x_dot;
            x_dot = x_dot + Tau * xacc;
            theta = theta + Tau * theta_dot;
            theta_dot = theta_dot + Tau * thetaacc;

            _state = new double[] { x, x_dot, theta, theta_dot };
            _steps++;

            bool done = x < -XThreshold || x > XThreshold ||
                        theta < -ThetaThresholdRadians || theta > ThetaThresholdRadians ||
                        _steps >= 500;

            double reward = 1.0;
            if (done && _steps < 500) reward = 0; // Failed

            return new StepResult
            {
                State = (double[])_state.Clone(),
                Reward = reward,
                Done = done,
                Info = ""
            };
        }
    }
}