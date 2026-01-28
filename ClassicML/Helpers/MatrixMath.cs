using System;
using System.Collections.Generic;
using System.Linq;

namespace ClassicML.Helpers
{
    // Helper sederhana untuk operasi matematika dan matriks tanpa library eksternal
    public static class MatrixMath
    {
        public static double DotProduct(double[] a, double[] b)
        {
            if (a.Length != b.Length) throw new Exception("Vector lengths must match");
            return a.Zip(b, (x, y) => x * y).Sum();
        }

        public static double Mean(double[] values) => values.Average();

        public static double Variance(double[] values)
        {
            double mean = Mean(values);
            return values.Select(v => Math.Pow(v - mean, 2)).Sum() / values.Length;
        }

        public static double EuclideanDistance(double[] point1, double[] point2)
        {
            double sum = 0;
            for (int i = 0; i < point1.Length; i++)
            {
                sum += Math.Pow(point1[i] - point2[i], 2);
            }
            return Math.Sqrt(sum);
        }

        public static double Sigmoid(double z)
        {
            return 1.0 / (1.0 + Math.Exp(-z));
        }

        // Simple Matrix Multiplication (A x B)
        public static double[,] Multiply(double[,] A, double[,] B)
        {
            int rA = A.GetLength(0);
            int cA = A.GetLength(1);
            int rB = B.GetLength(0);
            int cB = B.GetLength(1);

            if (cA != rB) throw new Exception("Matrix dimensions mismatch");

            double[,] result = new double[rA, cB];

            for (int i = 0; i < rA; i++)
            {
                for (int j = 0; j < cB; j++)
                {
                    for (int k = 0; k < cA; k++)
                    {
                        result[i, j] += A[i, k] * B[k, j];
                    }
                }
            }
            return result;
        }
    }
}
