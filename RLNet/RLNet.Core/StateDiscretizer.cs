using System;

namespace RLNet.Core
{
    public static class StateDiscretizer
    {
        // Helper to convert continuous values into discrete bins for Q-Learning
        public static int Discretize(double value, double min, double max, int bins)
        {
            if (value <= min) return 0;
            if (value >= max) return bins - 1;
            
            double span = max - min;
            double step = span / bins;
            return (int)((value - min) / step);
        }

        public static string CartPoleEncoder(double[] state)
        {
            // Cart Position: -2.4 to 2.4 (ignore mostly) -> 1 bin (we care more about angle)
            // Cart Velocity: -Inf to Inf -> 1 bin
            // Pole Angle: -0.209 to 0.209 (approx 12 deg)
            // Pole Velocity: -Inf to Inf
            
            // Simple bucketing
            int cartPosBin = Discretize(state[0], -2.4, 2.4, 1); 
            int cartVelBin = Discretize(state[1], -3.0, 3.0, 1);
            int angleBin = Discretize(state[2], -0.209, 0.209, 12); // High resolution for angle
            int angleVelBin = Discretize(state[3], -3.0, 3.0, 6);   // Medium resolution for angular velocity
            
            return $"{cartPosBin}_{cartVelBin}_{angleBin}_{angleVelBin}";
        }
        
        public static string LunarLanderEncoder(double[] state)
        {
            // X, Y, VX, VY, Angle, VAngle, Leg1, Leg2
            int x = Discretize(state[0], -1.0, 1.0, 5);
            int y = Discretize(state[1], 0.0, 1.5, 5);
            int vx = Discretize(state[2], -1.5, 1.5, 5);
            int vy = Discretize(state[3], -1.5, 1.5, 5);
            int ang = Discretize(state[4], -1.0, 1.0, 5);
            
            return $"{x}_{y}_{vx}_{vy}_{ang}";
        }
        
        public static string TradingEncoder(double[] state)
        {
            // [Balance, Shares, CurrentPrice]
            // We care about Shares held and maybe price trend?
            // This is tricky for Q-Table. Let's just bin Shares and Relative Price?
            // For simple random walk, optimal strategy might just be Buy Low Sell High?
            // Let's just return Shares Held for now as a super dumb agent
            return $"{(int)state[1]}"; 
        }
    }
}