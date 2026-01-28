using System;
using System.Collections.Generic;
using System.Linq;

namespace ClassicML.Algorithms.Reinforcement
{
    // --- Multi-Armed Bandit (Epsilon-Greedy) ---
    public class MultiArmedBandit
    {
        private int nArms;
        private double epsilon;
        private double[] qValues;
        private int[] actionCounts;
        private Random rand = new Random();

        public MultiArmedBandit(int nArms, double epsilon = 0.1)
        {
            this.nArms = nArms;
            this.epsilon = epsilon;
            qValues = new double[nArms];
            actionCounts = new int[nArms];
        }

        public int SelectAction()
        {
            if (rand.NextDouble() < epsilon)
            {
                return rand.Next(nArms); // Explore
            }
            else
            {
                // Exploit (argmax)
                double maxVal = qValues.Max();
                int maxIdx = Array.IndexOf(qValues, maxVal);
                return maxIdx;
            }
        }

        public void Update(int action, double reward)
        {
            actionCounts[action]++;
            // Q(a) = Q(a) + 1/N * (R - Q(a))
            qValues[action] += (1.0 / actionCounts[action]) * (reward - qValues[action]);
        }
    }

    // --- Q-Learning & SARSA (Tabular for Grid World) ---
    public class TabularRL
    {
        private Dictionary<(int state, int action), double> QTable;
        private double alpha; // Learning rate
        private double gamma; // Discount factor
        private double epsilon;
        private Random rand = new Random();
        private int nActions;

        public TabularRL(int nActions, double alpha = 0.1, double gamma = 0.9, double epsilon = 0.1)
        {
            this.nActions = nActions;
            this.alpha = alpha;
            this.gamma = gamma;
            this.epsilon = epsilon;
            QTable = new Dictionary<(int, int), double>();
        }

        private double GetQ(int state, int action)
        {
            if (!QTable.ContainsKey((state, action))) return 0.0;
            return QTable[(state, action)];
        }

        public int ChooseAction(int state)
        {
            if (rand.NextDouble() < epsilon)
            {
                return rand.Next(nActions);
            }
            else
            {
                double maxQ = double.NegativeInfinity;
                int bestAction = 0;
                for (int a = 0; a < nActions; a++)
                {
                    double q = GetQ(state, a);
                    if (q > maxQ)
                    {
                        maxQ = q;
                        bestAction = a;
                    }
                }
                return bestAction;
            }
        }

        public void QLearningUpdate(int state, int action, double reward, int nextState)
        {
            double maxNextQ = double.NegativeInfinity;
            for (int a = 0; a < nActions; a++)
            {
                double q = GetQ(nextState, a);
                if (q > maxNextQ) maxNextQ = q;
            }
            if (maxNextQ == double.NegativeInfinity) maxNextQ = 0;

            double currentQ = GetQ(state, action);
            // Q(s,a) = Q(s,a) + alpha * (r + gamma * max(Q(s',a')) - Q(s,a))
            double newQ = currentQ + alpha * (reward + gamma * maxNextQ - currentQ);
            
            QTable[(state, action)] = newQ;
        }

        public void SarsaUpdate(int state, int action, double reward, int nextState, int nextAction)
        {
            double currentQ = GetQ(state, action);
            double nextQ = GetQ(nextState, nextAction);

            // Q(s,a) = Q(s,a) + alpha * (r + gamma * Q(s',a') - Q(s,a))
            double newQ = currentQ + alpha * (reward + gamma * nextQ - currentQ);
            
            QTable[(state, action)] = newQ;
        }
    }
}
