using System;
using System.Collections.Generic;
using System.Linq;

namespace ClassicML.Algorithms.Supervised
{
    // --- Decision Tree (Simplified CART for Classification) ---
    public class DecisionTree
    {
        private class Node
        {
            public int FeatureIndex { get; set; }
            public double Threshold { get; set; }
            public Node Left { get; set; }
            public Node Right { get; set; }
            public int? Value { get; set; } // Leaf node value
        }

        private Node root;
        private int maxDepth;
        private int minSamplesSplit;

        public DecisionTree(int maxDepth = 10, int minSamplesSplit = 2)
        {
            this.maxDepth = maxDepth;
            this.minSamplesSplit = minSamplesSplit;
        }

        public void Fit(double[][] X, int[] y)
        {
            root = BuildTree(X, y, 0);
        }

        private Node BuildTree(double[][] X, int[] y, int depth)
        {
            int nSamples = X.Length;
            int nLabels = y.Distinct().Count();

            // Stopping criteria
            if (depth >= maxDepth || nLabels == 1 || nSamples < minSamplesSplit)
            {
                return new Node { Value = MostCommonLabel(y) };
            }

            int bestFeat = -1;
            double bestThresh = 0;
            double bestGain = -1;

            // Find best split (simplified: random sampling of thresholds could be used for speed, here we check all)
            int nFeatures = X[0].Length;
            for (int featIdx = 0; featIdx < nFeatures; featIdx++)
            {
                var thresholds = X.Select(row => row[featIdx]).Distinct().ToArray();
                foreach (var thresh in thresholds)
                {
                    double gain = InformationGain(y, X, featIdx, thresh);
                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        bestFeat = featIdx;
                        bestThresh = thresh;
                    }
                }
            }

            if (bestGain == -1) // No split found
            {
                return new Node { Value = MostCommonLabel(y) };
            }

            // Split
            var leftIndices = new List<int>();
            var rightIndices = new List<int>();

            for (int i = 0; i < nSamples; i++)
            {
                if (X[i][bestFeat] <= bestThresh) leftIndices.Add(i);
                else rightIndices.Add(i);
            }

            if (leftIndices.Count == 0 || rightIndices.Count == 0) return new Node { Value = MostCommonLabel(y) };

            var leftX = leftIndices.Select(i => X[i]).ToArray();
            var leftY = leftIndices.Select(i => y[i]).ToArray();
            var rightX = rightIndices.Select(i => X[i]).ToArray();
            var rightY = rightIndices.Select(i => y[i]).ToArray();

            return new Node
            {
                FeatureIndex = bestFeat,
                Threshold = bestThresh,
                Left = BuildTree(leftX, leftY, depth + 1),
                Right = BuildTree(rightX, rightY, depth + 1)
            };
        }

        private double InformationGain(int[] y, double[][] X, int featIdx, double threshold)
        {
            double parentEntropy = Entropy(y);

            var leftY = new List<int>();
            var rightY = new List<int>();

            for(int i=0; i<X.Length; i++)
            {
                if (X[i][featIdx] <= threshold) leftY.Add(y[i]);
                else rightY.Add(y[i]);
            }

            if (leftY.Count == 0 || rightY.Count == 0) return 0;

            double n = y.Length;
            double leftEntropy = Entropy(leftY.ToArray());
            double rightEntropy = Entropy(rightY.ToArray());

            double childEntropy = (leftY.Count / n) * leftEntropy + (rightY.Count / n) * rightEntropy;
            return parentEntropy - childEntropy;
        }

        private double Entropy(int[] y)
        {
            var counts = y.GroupBy(v => v).Select(g => g.Count());
            double entropy = 0;
            double n = y.Length;
            foreach (var count in counts)
            {
                double p = count / n;
                entropy -= p * Math.Log(p, 2);
            }
            return entropy;
        }

        private int MostCommonLabel(int[] y)
        {
            return y.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
        }

        public int Predict(double[] x)
        {
            Node node = root;
            while (node.Value == null)
            {
                if (x[node.FeatureIndex] <= node.Threshold)
                    node = node.Left;
                else
                    node = node.Right;
            }
            return node.Value.Value;
        }
    }

    // --- Random Forest (Ensemble of Decision Trees) ---
    public class RandomForest
    {
        private List<DecisionTree> trees;
        private int nTrees;
        private int maxDepth;
        private Random rand = new Random();

        public RandomForest(int nTrees = 5, int maxDepth = 5)
        {
            this.nTrees = nTrees;
            this.maxDepth = maxDepth;
            trees = new List<DecisionTree>();
        }

        public void Fit(double[][] X, int[] y)
        {
            for (int i = 0; i < nTrees; i++)
            {
                var tree = new DecisionTree(maxDepth);
                // Bootstrap sample
                var (X_sample, y_sample) = BootstrapSample(X, y);
                tree.Fit(X_sample, y_sample);
                trees.Add(tree);
            }
        }

        private (double[][], int[]) BootstrapSample(double[][] X, int[] y)
        {
            int nSamples = X.Length;
            var X_sample = new double[nSamples][];
            var y_sample = new int[nSamples];

            for (int i = 0; i < nSamples; i++)
            {
                int idx = rand.Next(nSamples);
                X_sample[i] = X[idx];
                y_sample[i] = y[idx];
            }
            return (X_sample, y_sample);
        }

        public int Predict(double[] x)
        {
            var predictions = trees.Select(t => t.Predict(x)).ToArray();
            return predictions.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;
        }
    }
}
