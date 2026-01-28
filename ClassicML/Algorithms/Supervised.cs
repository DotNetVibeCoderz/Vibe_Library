using System;
using System.Collections.Generic;
using System.Linq;
using ClassicML.Helpers;

namespace ClassicML.Algorithms.Supervised
{
    // --- 1. Linear Regression ---
    public class SimpleLinearRegression
    {
        public double Slope { get; private set; }
        public double Intercept { get; private set; }

        public void Train(double[] x, double[] y)
        {
            double xMean = MatrixMath.Mean(x);
            double yMean = MatrixMath.Mean(y);

            double numerator = 0;
            double denominator = 0;

            for (int i = 0; i < x.Length; i++)
            {
                numerator += (x[i] - xMean) * (y[i] - yMean);
                denominator += Math.Pow(x[i] - xMean, 2);
            }

            Slope = numerator / denominator;
            Intercept = yMean - (Slope * xMean);
        }

        public double Predict(double x)
        {
            return Slope * x + Intercept;
        }
    }

    // --- 2. Logistic Regression ---
    public class LogisticRegression
    {
        private double[] weights = Array.Empty<double>();
        private double bias;
        private double learningRate;
        private int iterations;

        public LogisticRegression(double learningRate = 0.01, int iterations = 1000)
        {
            this.learningRate = learningRate;
            this.iterations = iterations;
        }

        public void Train(double[][] X, int[] y)
        {
            int nSamples = X.Length;
            int nFeatures = X[0].Length;
            weights = new double[nFeatures];
            bias = 0;

            for (int i = 0; i < iterations; i++)
            {
                // Gradient Descent
                for (int j = 0; j < nSamples; j++)
                {
                    double linearModel = MatrixMath.DotProduct(X[j], weights) + bias;
                    double yPredicted = MatrixMath.Sigmoid(linearModel);

                    double error = yPredicted - y[j];

                    for (int k = 0; k < nFeatures; k++)
                    {
                        weights[k] -= learningRate * error * X[j][k];
                    }
                    bias -= learningRate * error;
                }
            }
        }

        public int Predict(double[] x)
        {
            if (weights.Length == 0) throw new InvalidOperationException("Model not trained");
            double linearModel = MatrixMath.DotProduct(x, weights) + bias;
            return MatrixMath.Sigmoid(linearModel) >= 0.5 ? 1 : 0;
        }
    }

    // --- 3. k-Nearest Neighbors (k-NN) ---
    public class KNN
    {
        private int k;
        private List<double[]> X_train;
        private List<int> y_train;

        public KNN(int k = 3)
        {
            this.k = k;
            X_train = new List<double[]>();
            y_train = new List<int>();
        }

        public void Fit(double[][] X, int[] y)
        {
            X_train.AddRange(X);
            y_train.AddRange(y);
        }

        public int Predict(double[] x)
        {
            var distances = new List<(double dist, int label)>();

            for (int i = 0; i < X_train.Count; i++)
            {
                double dist = MatrixMath.EuclideanDistance(x, X_train[i]);
                distances.Add((dist, y_train[i]));
            }

            var kNearest = distances.OrderBy(d => d.dist).Take(k);
            
            // Majority vote
            return kNearest.GroupBy(n => n.label)
                           .OrderByDescending(g => g.Count())
                           .First().Key;
        }
    }

    // --- 4. Naive Bayes (Gaussian) ---
    public class GaussianNaiveBayes
    {
        private Dictionary<int, double[]> means = new Dictionary<int, double[]>();
        private Dictionary<int, double[]> variances = new Dictionary<int, double[]>();
        private Dictionary<int, double> priors = new Dictionary<int, double>();
        private int[]? classes;

        public void Fit(double[][] X, int[] y)
        {
            int nSamples = X.Length;
            int nFeatures = X[0].Length;
            classes = y.Distinct().ToArray();

            foreach (var c in classes)
            {
                var X_c = X.Where((val, idx) => y[idx] == c).ToArray();
                priors[c] = (double)X_c.Length / nSamples;

                means[c] = new double[nFeatures];
                variances[c] = new double[nFeatures];

                for (int feature = 0; feature < nFeatures; feature++)
                {
                    double[] featureValues = X_c.Select(row => row[feature]).ToArray();
                    means[c][feature] = MatrixMath.Mean(featureValues);
                    variances[c][feature] = MatrixMath.Variance(featureValues);
                }
            }
        }

        public int Predict(double[] x)
        {
            if (classes == null) throw new InvalidOperationException("Model not trained");

            int bestClass = -1;
            double maxPosterior = double.NegativeInfinity;

            foreach (var c in classes)
            {
                double posterior = Math.Log(priors[c]);
                for (int i = 0; i < x.Length; i++)
                {
                    double mean = means[c][i];
                    double var = variances[c][i];
                    if (var == 0) var = 1e-9; // prevent division by zero

                    double probability = (1.0 / Math.Sqrt(2 * Math.PI * var)) *
                                         Math.Exp(-Math.Pow(x[i] - mean, 2) / (2 * var));
                    
                    posterior += Math.Log(probability + 1e-9);
                }

                if (posterior > maxPosterior)
                {
                    maxPosterior = posterior;
                    bestClass = c;
                }
            }
            return bestClass;
        }
    }

    // --- 5. Support Vector Machine (Simple Linear SVM using Gradient Descent) ---
    public class LinearSVM
    {
        private double[] weights = Array.Empty<double>();
        private double bias;
        private double learningRate = 0.001;
        private double lambda = 0.01; // regularization parameter
        private int iterations = 1000;

        public void Fit(double[][] X, int[] y)
        {
            int nFeatures = X[0].Length;
            weights = new double[nFeatures];
            bias = 0;
            
            // SVM expects labels -1 and 1
            int[] y_svm = y.Select(v => v <= 0 ? -1 : 1).ToArray();

            for (int i = 0; i < iterations; i++)
            {
                for (int idx = 0; idx < X.Length; idx++)
                {
                    double condition = y_svm[idx] * (MatrixMath.DotProduct(X[idx], weights) - bias);
                    
                    if (condition >= 1)
                    {
                        // Gradient only from regularization
                        for (int j = 0; j < nFeatures; j++)
                            weights[j] -= learningRate * (2 * lambda * weights[j]);
                    }
                    else
                    {
                        // Gradient from loss + regularization
                        // Corrected formula implementation
                        for (int j = 0; j < nFeatures; j++)
                        {
                            // deriv = 2*lambda*w - y_i * x_i
                            double gradient = 2 * lambda * weights[j] - y_svm[idx] * X[idx][j];
                            weights[j] -= learningRate * gradient;
                        }
                        
                        bias -= learningRate * y_svm[idx];
                    }
                }
            }
        }

        public int Predict(double[] x)
        {
            if (weights.Length == 0) throw new InvalidOperationException("Model not trained");
            double output = MatrixMath.DotProduct(x, weights) - bias;
            return Math.Sign(output) == -1 ? 0 : 1; 
        }
    }
}
