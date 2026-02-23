using System;
using System.Collections.Generic;

namespace RLNet.Core
{
    public class TradingEnvironment : IEnvironment
    {
        private List<double> _prices;
        private int _currentStep;
        private double _balance;
        private int _sharesHeld;
        private double _netWorth;
        private double _initialBalance = 10000;
        private Random _random;

        public TradingEnvironment()
        {
            _random = new Random();
            GeneratePrices();
            Reset();
        }

        private void GeneratePrices()
        {
            // Generate a random walk price series
            _prices = new List<double>();
            double price = 100;
            for (int i = 0; i < 1000; i++)
            {
                _prices.Add(price);
                double change = (_random.NextDouble() - 0.5) * 5; // +/- 2.5
                price += change;
                if (price < 10) price = 10;
            }
        }

        public int GetActionSpaceSize() => 3; // 0: Hold, 1: Buy, 2: Sell
        public int[] GetObservationSpaceShape() => new int[] { 3 }; // [Balance, Shares, CurrentPrice]

        public StepResult Reset()
        {
            _currentStep = 0;
            _balance = _initialBalance;
            _sharesHeld = 0;
            _netWorth = _initialBalance;
            // Regenerate prices for variety? or Keep same? Let's keep for now but we could re-gen.
            GeneratePrices(); // New market scenario
            
            return GetState(0, false);
        }

        public StepResult Step(int action)
        {
            double currentPrice = _prices[_currentStep];
            double prevNetWorth = _netWorth;

            // Execute Trade
            if (action == 1) // Buy 1 Share
            {
                if (_balance >= currentPrice)
                {
                    _balance -= currentPrice;
                    _sharesHeld++;
                }
            }
            else if (action == 2) // Sell 1 Share
            {
                if (_sharesHeld > 0)
                {
                    _balance += currentPrice;
                    _sharesHeld--;
                }
            }
            // 0 = Hold

            _currentStep++;
            bool done = _currentStep >= _prices.Count - 1;

            double newPrice = done ? currentPrice : _prices[_currentStep];
            _netWorth = _balance + (_sharesHeld * newPrice);

            // Reward is change in Net Worth
            // To make it learn, we reward profit, punish loss
            double reward = (_netWorth - prevNetWorth); 

            return GetState(reward, done);
        }

        private StepResult GetState(double reward, bool done)
        {
            double price = (_currentStep < _prices.Count) ? _prices[_currentStep] : 0;
            return new StepResult
            {
                State = new double[] { _balance, (double)_sharesHeld, price },
                Reward = reward,
                Done = done,
                Info = $"NW: {_netWorth:F2}"
            };
        }
        
        // Helper for visualization
        public List<double> GetPriceHistory() => _prices;
        public int CurrentStep => _currentStep;
        public int Shares => _sharesHeld;
        public double Balance => _balance;
        public double NetWorth => _netWorth;
    }
}