using System;

namespace RLNet.Core
{
    public class GridWorldEnvironment : IEnvironment
    {
        private readonly int[,] _grid;
        private int _agentX;
        private int _agentY;
        private readonly int _width;
        private readonly int _height;

        public int Width => _width;
        public int Height => _height;
        public int AgentX => _agentX;
        public int AgentY => _agentY;

        public GridWorldEnvironment(int width = 5, int height = 5)
        {
            _width = width;
            _height = height;
            _grid = new int[width, height];
            _grid[width - 1, height - 1] = 2; // Goal
            _grid[1, 1] = 3; // Trap
            _grid[2, 2] = 3;
            _grid[3, 1] = 3;
        }

        public int GetActionSpaceSize() => 4;
        public int[] GetObservationSpaceShape() => new int[] { 2 };

        public StepResult Reset()
        {
            _agentX = 0;
            _agentY = 0;
            return GetResult(0, false, "Reset");
        }

        public StepResult Step(int action)
        {
            int nextX = _agentX;
            int nextY = _agentY;

            switch (action)
            {
                case 0: nextY--; break; // Up
                case 1: nextY++; break; // Down
                case 2: nextX--; break; // Left
                case 3: nextX++; break; // Right
            }

            if (nextX < 0 || nextX >= _width || nextY < 0 || nextY >= _height)
                return GetResult(-1, false, "Hit Wall");

            _agentX = nextX;
            _agentY = nextY;

            int cell = _grid[_agentX, _agentY];
            double reward = -0.1;
            bool done = false;

            if (cell == 2) { reward = 10; done = true; }
            else if (cell == 3) { reward = -10; done = true; }

            return GetResult(reward, done, "Moved");
        }

        private StepResult GetResult(double reward, bool done, string info)
        {
            return new StepResult
            {
                State = new double[] { _agentX, _agentY },
                Reward = reward,
                Done = done,
                Info = info
            };
        }

        public int GetCellType(int x, int y)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height) return -1;
            return _grid[x, y];
        }
    }
}