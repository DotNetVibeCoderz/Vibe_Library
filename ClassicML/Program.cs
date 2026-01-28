using System;
using System.Collections.Generic;
using ClassicML.Algorithms.Supervised;
using ClassicML.Algorithms.Unsupervised;
using ClassicML.Algorithms.Neural;
using ClassicML.Algorithms.Reinforcement;

namespace ClassicML
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("   ClassicML - Machine Learning from Scratch");
            Console.WriteLine("   Built by Jacky the code bender");
            Console.WriteLine("=================================================");
            Console.WriteLine();

            RunSupervisedDemo();
            RunUnsupervisedDemo();
            RunNeuralDemo();
            RunRLDemo();

            Console.WriteLine("\nAll demos finished. Press any key to exit.");
            Console.ReadKey();
        }

        static void RunSupervisedDemo()
        {
            Console.WriteLine("--- [Supervised Learning Demos] ---");

            // 1. Linear Regression
            Console.WriteLine("\n1. Linear Regression (House Price Prediction)");
            var lr = new SimpleLinearRegression();
            double[] sizes = { 1000, 1500, 2000, 2500, 3000 };
            double[] prices = { 200000, 300000, 400000, 500000, 600000 };
            lr.Train(sizes, prices);
            double testSize = 1800;
            Console.WriteLine($"   Slope: {lr.Slope:F2}, Intercept: {lr.Intercept:F2}");
            Console.WriteLine($"   Predicted price for {testSize} sqft: ${lr.Predict(testSize):F2}");

            // 2. Logistic Regression
            Console.WriteLine("\n2. Logistic Regression (Pass/Fail based on study hours)");
            var logReg = new LogisticRegression();
            double[][] hours = { new[] { 0.5 }, new[] { 0.75 }, new[] { 1.0 }, new[] { 1.25 }, new[] { 1.5 }, new[] { 2.0 }, new[] { 3.0 }, new[] { 4.0 } };
            int[] pass = { 0, 0, 0, 0, 0, 1, 1, 1 };
            logReg.Train(hours, pass);
            double[] testHours = { 2.5 };
            Console.WriteLine($"   Prediction for 2.5 hours: {(logReg.Predict(testHours) == 1 ? "Pass" : "Fail")}");

            // 3. KNN
            Console.WriteLine("\n3. k-NN (Simple 2D Classification)");
            var knn = new KNN(k: 3);
            double[][] knnData = { 
                new[] { 1.0, 1.0 }, new[] { 1.1, 1.1 }, // Class 0
                new[] { 5.0, 5.0 }, new[] { 5.1, 5.1 }  // Class 1
            };
            int[] knnLabels = { 0, 0, 1, 1 };
            knn.Fit(knnData, knnLabels);
            double[] knnTest = { 1.2, 1.2 };
            Console.WriteLine($"   Point [1.2, 1.2] predicted Class: {knn.Predict(knnTest)}");

            // 4. Decision Tree
            Console.WriteLine("\n4. Decision Tree (Simple Binary Classification)");
            var dt = new DecisionTree(maxDepth: 3);
            // X: [Age, Income]
            double[][] dtData = { 
                new[] { 20.0, 20000.0 }, new[] { 21.0, 22000.0 }, // No Buy (0)
                new[] { 40.0, 60000.0 }, new[] { 45.0, 80000.0 }  // Buy (1)
            };
            int[] dtLabels = { 0, 0, 1, 1 };
            dt.Fit(dtData, dtLabels);
            double[] dtTest = { 38.0, 55000.0 };
            Console.WriteLine($"   Person Age 38, Income 55k predicted to buy: {(dt.Predict(dtTest) == 1 ? "Yes" : "No")}");
        }

        static void RunUnsupervisedDemo()
        {
            Console.WriteLine("\n--- [Unsupervised Learning Demos] ---");

            // 1. KMeans
            Console.WriteLine("\n1. k-Means Clustering");
            double[][] points = { 
                new[] { 1.0, 1.0 }, new[] { 1.5, 2.0 }, // Cluster A
                new[] { 10.0, 10.0 }, new[] { 10.5, 11.0 } // Cluster B
            };
            var kmeans = new KMeans(k: 2);
            var labels = kmeans.FitPredict(points);
            Console.WriteLine($"   Points clustered labels: [{string.Join(", ", labels)}]");
            Console.WriteLine("   Centroids:");
            foreach(var c in kmeans.Centroids) Console.WriteLine($"     [{c[0]:F2}, {c[1]:F2}]");

            // 2. PCA
            Console.WriteLine("\n2. PCA (Dimensionality Reduction 2D -> 1D)");
            var pca = new SimplifiedPCA();
            // Data roughly correlated y = x
            double[][] pcaData = {
                new[] { 2.5, 2.4 }, new[] { 0.5, 0.7 }, new[] { 2.2, 2.9 },
                new[] { 1.9, 2.2 }, new[] { 3.1, 3.0 }
            };
            pca.Fit(pcaData);
            Console.WriteLine("   First Principal Component (Eigenvector):");
            Console.WriteLine($"     [{pca.FirstComponent[0]:F4}, {pca.FirstComponent[1]:F4}]");
            var transformed = pca.Transform(pcaData);
            Console.WriteLine("   Transformed Data (1D): " + string.Join(", ", transformed));
        }

        static void RunNeuralDemo()
        {
            Console.WriteLine("\n--- [Neural Network Demos] ---");

            // 1. Perceptron (AND Gate)
            Console.WriteLine("\n1. Perceptron (AND Gate Logic)");
            var p = new Perceptron(learningRate: 0.1, epochs: 20);
            double[][] andX = { new[] { 0.0, 0.0 }, new[] { 0.0, 1.0 }, new[] { 1.0, 0.0 }, new[] { 1.0, 1.0 } };
            int[] andY = { 0, 0, 0, 1 };
            p.Train(andX, andY);
            Console.WriteLine($"   0 AND 1 = {p.Predict(new[] { 0.0, 1.0 })}");
            Console.WriteLine($"   1 AND 1 = {p.Predict(new[] { 1.0, 1.0 })}");

            // 2. MLP (XOR Problem - Non-linear)
            Console.WriteLine("\n2. MLP (XOR Gate Logic - solving non-linearity)");
            // 2 inputs, 4 hidden neurons, 1 output
            var mlp = new MLP(2, 4, 1, learningRate: 0.5);
            double[][] xorX = { new[] { 0.0, 0.0 }, new[] { 0.0, 1.0 }, new[] { 1.0, 0.0 }, new[] { 1.0, 1.0 } };
            double[][] xorY = { new[] { 0.0 }, new[] { 1.0 }, new[] { 1.0 }, new[] { 0.0 } };
            
            Console.Write("   Training MLP (this might take a moment)... ");
            mlp.Train(xorX, xorY, 5000); 
            Console.WriteLine("Done.");

            Console.WriteLine($"   0 XOR 0 -> {mlp.Predict(new[] { 0.0, 0.0 })[0]:F4} (Expected 0)");
            Console.WriteLine($"   0 XOR 1 -> {mlp.Predict(new[] { 0.0, 1.0 })[0]:F4} (Expected 1)");
            Console.WriteLine($"   1 XOR 0 -> {mlp.Predict(new[] { 1.0, 0.0 })[0]:F4} (Expected 1)");
            Console.WriteLine($"   1 XOR 1 -> {mlp.Predict(new[] { 1.0, 1.0 })[0]:F4} (Expected 0)");
        }

        static void RunRLDemo()
        {
            Console.WriteLine("\n--- [Reinforcement Learning Demos] ---");

            // 1. Multi Armed Bandit
            Console.WriteLine("\n1. Multi-Armed Bandit (Finding the best slot machine)");
            int arms = 3;
            double[] trueProbabilities = { 0.2, 0.5, 0.8 }; // Arm 2 is best
            var bandit = new MultiArmedBandit(arms, epsilon: 0.1);
            Console.WriteLine("   True probabilities: [0.2, 0.5, 0.8]");
            Console.WriteLine("   Simulating 1000 pulls...");

            var rng = new Random();
            for (int i = 0; i < 1000; i++)
            {
                int action = bandit.SelectAction();
                // Simulate reward based on probability
                double reward = rng.NextDouble() < trueProbabilities[action] ? 1.0 : 0.0;
                bandit.Update(action, reward);
            }
            // Best arm should be selected most often roughly
            Console.WriteLine("   Training done. Bandit should prefer Arm 2.");

            // 2. Q-Learning
            Console.WriteLine("\n2. Q-Learning (Simple 1D Grid World)");
            // State: 0-1-2-3-4 (Goal)
            // Actions: 0 (Left), 1 (Right)
            var ql = new TabularRL(nActions: 2);
            int goalState = 4;
            Console.WriteLine("   Grid: [Start] -- [1] -- [2] -- [3] -- [Goal]");
            
            for (int ep = 0; ep < 500; ep++)
            {
                int state = 0;
                while (state != goalState)
                {
                    int action = ql.ChooseAction(state);
                    // Environment logic
                    int nextState = state;
                    if (action == 0) nextState = Math.Max(0, state - 1); // Left
                    else nextState = Math.Min(goalState, state + 1); // Right
                    
                    double reward = (nextState == goalState) ? 1.0 : 0.0;
                    
                    ql.QLearningUpdate(state, action, reward, nextState);
                    state = nextState;
                }
            }
            Console.WriteLine("   Q-Table Learned. Testing path from 0:");
            int curr = 0;
            Console.Write("   Path: ");
            while(curr != goalState)
            {
                Console.Write($"[{curr}] -> ");
                // Greedy choice
                int action = ql.ChooseAction(curr); // Epsilon is low, so mostly greedy
                if (action == 0) curr = Math.Max(0, curr - 1);
                else curr = Math.Min(goalState, curr + 1);
            }
            Console.WriteLine("[Goal]");
        }
    }
}
