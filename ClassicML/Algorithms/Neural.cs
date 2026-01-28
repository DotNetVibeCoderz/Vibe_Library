using System;
using System.Linq;
using ClassicML.Helpers;

namespace ClassicML.Algorithms.Neural
{
    // --- Perceptron ---
    public class Perceptron
    {
        private double[] weights;
        private double bias;
        private double learningRate;
        private int epochs;

        public Perceptron(double learningRate = 0.1, int epochs = 100)
        {
            this.learningRate = learningRate;
            this.epochs = epochs;
        }

        public void Train(double[][] X, int[] y)
        {
            int nFeat = X[0].Length;
            weights = new double[nFeat];
            bias = 0;

            for (int e = 0; e < epochs; e++)
            {
                for (int i = 0; i < X.Length; i++)
                {
                    double linearOutput = MatrixMath.DotProduct(X[i], weights) + bias;
                    int prediction = linearOutput >= 0 ? 1 : 0;
                    double error = y[i] - prediction;

                    for (int j = 0; j < nFeat; j++)
                    {
                        weights[j] += learningRate * error * X[i][j];
                    }
                    bias += learningRate * error;
                }
            }
        }

        public int Predict(double[] x)
        {
            double output = MatrixMath.DotProduct(x, weights) + bias;
            return output >= 0 ? 1 : 0;
        }
    }

    // --- Multilayer Perceptron (MLP) with Backpropagation ---
    // Architecture: Input -> Hidden (Sigmoid) -> Output (Sigmoid)
    public class MLP
    {
        private int inputSize;
        private int hiddenSize;
        private int outputSize;
        
        // Weights
        private double[,] w1; // Input -> Hidden
        private double[] b1;
        private double[,] w2; // Hidden -> Output
        private double[] b2;

        private double learningRate;

        public MLP(int inputSize, int hiddenSize, int outputSize, double learningRate = 0.1)
        {
            this.inputSize = inputSize;
            this.hiddenSize = hiddenSize;
            this.outputSize = outputSize;
            this.learningRate = learningRate;

            InitializeWeights();
        }

        private void InitializeWeights()
        {
            Random rand = new Random();
            w1 = new double[inputSize, hiddenSize];
            b1 = new double[hiddenSize];
            w2 = new double[hiddenSize, outputSize];
            b2 = new double[outputSize];

            for (int i = 0; i < inputSize; i++)
                for (int j = 0; j < hiddenSize; j++) w1[i, j] = rand.NextDouble() - 0.5;

            for (int i = 0; i < hiddenSize; i++)
                for (int j = 0; j < outputSize; j++) w2[i, j] = rand.NextDouble() - 0.5;
        }

        public void Train(double[][] X, double[][] y, int epochs)
        {
            for (int e = 0; e < epochs; e++)
            {
                for (int i = 0; i < X.Length; i++)
                {
                    // --- Forward Pass ---
                    // Hidden layer
                    double[] hiddenInput = new double[hiddenSize];
                    double[] hiddenOutput = new double[hiddenSize];
                    for (int h = 0; h < hiddenSize; h++)
                    {
                        double sum = 0;
                        for (int inIdx = 0; inIdx < inputSize; inIdx++) sum += X[i][inIdx] * w1[inIdx, h];
                        sum += b1[h];
                        hiddenInput[h] = sum;
                        hiddenOutput[h] = Sigmoid(sum);
                    }

                    // Output layer
                    double[] finalInput = new double[outputSize];
                    double[] finalOutput = new double[outputSize];
                    for (int o = 0; o < outputSize; o++)
                    {
                        double sum = 0;
                        for (int h = 0; h < hiddenSize; h++) sum += hiddenOutput[h] * w2[h, o];
                        sum += b2[o];
                        finalInput[o] = sum;
                        finalOutput[o] = Sigmoid(sum);
                    }

                    // --- Backward Pass (Backpropagation) ---
                    // Output Error (MSE derivative * Sigmoid derivative)
                    double[] outputGradients = new double[outputSize];
                    for (int o = 0; o < outputSize; o++)
                    {
                        double error = y[i][o] - finalOutput[o];
                        outputGradients[o] = error * SigmoidDerivative(finalOutput[o]);
                    }

                    // Hidden Error
                    double[] hiddenGradients = new double[hiddenSize];
                    for (int h = 0; h < hiddenSize; h++)
                    {
                        double error = 0;
                        for (int o = 0; o < outputSize; o++) error += outputGradients[o] * w2[h, o];
                        hiddenGradients[h] = error * SigmoidDerivative(hiddenOutput[h]);
                    }

                    // Update Weights
                    // Hidden -> Output
                    for (int h = 0; h < hiddenSize; h++)
                    {
                        for (int o = 0; o < outputSize; o++)
                        {
                            w2[h, o] += learningRate * outputGradients[o] * hiddenOutput[h];
                        }
                    }
                    for (int o = 0; o < outputSize; o++) b2[o] += learningRate * outputGradients[o];

                    // Input -> Hidden
                    for (int inIdx = 0; inIdx < inputSize; inIdx++)
                    {
                        for (int h = 0; h < hiddenSize; h++)
                        {
                            w1[inIdx, h] += learningRate * hiddenGradients[h] * X[i][inIdx];
                        }
                    }
                    for (int h = 0; h < hiddenSize; h++) b1[h] += learningRate * hiddenGradients[h];
                }
            }
        }

        public double[] Predict(double[] x)
        {
            double[] hiddenOutput = new double[hiddenSize];
            for (int h = 0; h < hiddenSize; h++)
            {
                double sum = 0;
                for (int inIdx = 0; inIdx < inputSize; inIdx++) sum += x[inIdx] * w1[inIdx, h];
                sum += b1[h];
                hiddenOutput[h] = Sigmoid(sum);
            }

            double[] finalOutput = new double[outputSize];
            for (int o = 0; o < outputSize; o++)
            {
                double sum = 0;
                for (int h = 0; h < hiddenSize; h++) sum += hiddenOutput[h] * w2[h, o];
                sum += b2[o];
                finalOutput[o] = Sigmoid(sum);
            }
            return finalOutput;
        }

        private double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp(-x));
        private double SigmoidDerivative(double x) => x * (1 - x); // Assumes x is already sigmoid output
    }
}
