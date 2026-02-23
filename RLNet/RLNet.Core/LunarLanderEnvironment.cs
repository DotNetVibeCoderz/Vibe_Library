using System;

namespace RLNet.Core
{
    // Simplified Lunar Lander
    public class LunarLanderEnvironment : IEnvironment
    {
        private double _x, _y, _vx, _vy, _angle, _vAngle;
        private Random _random;
        private const double Gravity = -0.005; // Lower gravity for easier control
        private const double MainEnginePower = 0.015;
        private const double SideEnginePower = 0.005;
        private int _steps;

        public double X => _x;
        public double Y => _y;
        public double Angle => _angle;
        public bool MainEngineFiring { get; private set; }
        public bool LeftEngineFiring { get; private set; }
        public bool RightEngineFiring { get; private set; }

        public LunarLanderEnvironment()
        {
            _random = new Random();
            Reset();
        }

        public int GetActionSpaceSize() => 4; // 0: None, 1: Main, 2: Left, 3: Right
        public int[] GetObservationSpaceShape() => new int[] { 6 }; // X, Y, VX, VY, Ang, VAng

        public StepResult Reset()
        {
            _x = 0; // Center
            _y = 1.0; // Top
            _vx = (_random.NextDouble() - 0.5) * 0.2; // Random initial drift
            _vy = 0;
            _angle = 0;
            _vAngle = 0;
            _steps = 0;
            return GetState(0, false);
        }

        public StepResult Step(int action)
        {
            MainEngineFiring = false;
            LeftEngineFiring = false;
            RightEngineFiring = false;

            // Physics
            double accelX = 0;
            double accelY = Gravity;
            double accelAng = 0;

            // 0: Do Nothing
            // 1: Fire Main Engine (Push Up)
            if (action == 1)
            {
                accelX += Math.Sin(_angle) * MainEnginePower;
                accelY += Math.Cos(_angle) * MainEnginePower;
                MainEngineFiring = true;
            }
            // 2: Fire Left Engine (Push Right, Rotate CW)
            else if (action == 2)
            {
                accelX += Math.Cos(_angle) * SideEnginePower; 
                accelAng -= 0.002; // Torque
                LeftEngineFiring = true;
            }
            // 3: Fire Right Engine (Push Left, Rotate CCW)
            else if (action == 3)
            {
                accelX -= Math.Cos(_angle) * SideEnginePower;
                accelAng += 0.002;
                RightEngineFiring = true;
            }

            _vx += accelX;
            _vy += accelY;
            _vAngle += accelAng;

            _x += _vx;
            _y += _vy;
            _angle += _vAngle;
            
            // Dampening
            _vAngle *= 0.98;

            _steps++;
            
            // Check Done
            bool done = false;
            double reward = -0.1; // Fuel cost / living penalty

            // Crash or Out of bounds
            if (_y < 0 || Math.Abs(_x) > 1.0 || _y > 1.5)
            {
                done = true;
                if (_y < 0 && Math.Abs(_x) < 0.2 && Math.Abs(_angle) < 0.2 && Math.Abs(_vy) < 0.1)
                {
                    reward = 100; // Landed safely
                }
                else
                {
                    reward = -100; // Crashed
                }
            }

            // Simple shaping
            if (!done)
            {
                 // Encourage being close to center (x=0) and low velocity
                 reward -= Math.Abs(_x) * 0.1;
            }

            return GetState(reward, done);
        }

        private StepResult GetState(double reward, bool done)
        {
            return new StepResult
            {
                State = new double[] { _x, _y, _vx, _vy, _angle, _vAngle },
                Reward = reward,
                Done = done,
                Info = ""
            };
        }
    }
}