using System;
using System.Collections.Generic;
using System.Linq;

namespace RLNet.Core
{
    public class QTableAgent
    {
        private Dictionary<string, double[]> _qTable;
        private readonly int _actionSize;
        private readonly double _learningRate;
        private readonly double _discountFactor;
        private double _epsilon;
        private readonly Random _random;
        
        // Delegate to convert state to string key
        private readonly Func<double[], string> _stateEncoder;

        public double Epsilon => _epsilon;

        public QTableAgent(int actionSize, Func<double[], string>? stateEncoder = null, double learningRate = 0.1, double discountFactor = 0.99, double epsilon = 1.0)
        {
            _actionSize = actionSize;
            _learningRate = learningRate;
            _discountFactor = discountFactor;
            _epsilon = epsilon;
            _qTable = new Dictionary<string, double[]>();
            _random = new Random();
            
            // Default encoder: join values with comma (good for discrete integers)
            _stateEncoder = stateEncoder ?? ((s) => string.Join(",", s));
        }

        public int GetAction(double[] state)
        {
            if (_random.NextDouble() < _epsilon)
                return _random.Next(_actionSize);

            string key = _stateEncoder(state);
            if (!_qTable.ContainsKey(key)) return _random.Next(_actionSize);

            double[] qValues = _qTable[key];
            double maxVal = qValues.Max();
            var bestActions = qValues.Select((val, idx) => new { val, idx })
                                     .Where(x => Math.Abs(x.val - maxVal) < 1e-5)
                                     .Select(x => x.idx)
                                     .ToArray();
            
            return bestActions[_random.Next(bestActions.Length)];
        }

        public void Train(double[] state, int action, double reward, double[] nextState, bool done)
        {
            string stateKey = _stateEncoder(state);
            string nextStateKey = _stateEncoder(nextState);

            if (!_qTable.ContainsKey(stateKey)) _qTable[stateKey] = new double[_actionSize];
            if (!_qTable.ContainsKey(nextStateKey)) _qTable[nextStateKey] = new double[_actionSize];

            double currentQ = _qTable[stateKey][action];
            double maxNextQ = done ? 0 : _qTable[nextStateKey].Max();

            double newQ = currentQ + _learningRate * (reward + _discountFactor * maxNextQ - currentQ);
            _qTable[stateKey][action] = newQ;
        }

        public void DecayEpsilon(double decayRate, double minEpsilon)
        {
            _epsilon = Math.Max(minEpsilon, _epsilon * decayRate);
        }
    }
}