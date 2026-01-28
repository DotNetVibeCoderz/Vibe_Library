using System;
using System.Collections.Generic;
using System.Linq;
using ClassicML.Helpers;

namespace ClassicML.Algorithms.Unsupervised
{
    // --- k-Means Clustering ---
    public class KMeans
    {
        private int k;
        private int maxIter;
        public List<double[]> Centroids { get; private set; }

        public KMeans(int k = 3, int maxIter = 100)
        {
            this.k = k;
            this.maxIter = maxIter;
        }

        public int[] FitPredict(double[][] X)
        {
            int nSamples = X.Length;
            int nFeatures = X[0].Length;
            var rand = new Random();

            // Initialize centroids randomly
            Centroids = new List<double[]>();
            for (int i = 0; i < k; i++)
            {
                Centroids.Add(X[rand.Next(nSamples)]);
            }

            int[] labels = new int[nSamples];
            for (int iter = 0; iter < maxIter; iter++)
            {
                // Assign clusters
                bool changed = false;
                for (int i = 0; i < nSamples; i++)
                {
                    int bestCluster = -1;
                    double minDist = double.MaxValue;
                    for (int j = 0; j < k; j++)
                    {
                        double dist = MatrixMath.EuclideanDistance(X[i], Centroids[j]);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            bestCluster = j;
                        }
                    }
                    if (labels[i] != bestCluster) changed = true;
                    labels[i] = bestCluster;
                }

                if (!changed) break;

                // Update centroids
                for (int j = 0; j < k; j++)
                {
                    var clusterPoints = X.Where((x, idx) => labels[idx] == j).ToArray();
                    if (clusterPoints.Length > 0)
                    {
                        double[] newCentroid = new double[nFeatures];
                        for (int f = 0; f < nFeatures; f++)
                        {
                            newCentroid[f] = clusterPoints.Select(p => p[f]).Average();
                        }
                        Centroids[j] = newCentroid;
                    }
                }
            }
            return labels;
        }
    }

    // --- Principal Component Analysis (Simplified: First Principal Component via Power Iteration) ---
    public class SimplifiedPCA
    {
        public double[] FirstComponent { get; private set; }

        public void Fit(double[][] X)
        {
            // 1. Standardize Data
            int nSamples = X.Length;
            int nFeatures = X[0].Length;
            double[][] X_std = new double[nSamples][];
            
            for(int i=0; i<nSamples; i++) X_std[i] = new double[nFeatures];

            for(int j=0; j<nFeatures; j++)
            {
                double[] col = X.Select(row => row[j]).ToArray();
                double mean = MatrixMath.Mean(col);
                double std = Math.Sqrt(MatrixMath.Variance(col));
                if(std == 0) std = 1;

                for(int i=0; i<nSamples; i++)
                {
                    X_std[i][j] = (X[i][j] - mean) / std;
                }
            }

            // 2. Covariance Matrix (Approximate)
            double[,] cov = new double[nFeatures, nFeatures];
            for(int j=0; j<nFeatures; j++)
            {
                for(int k=0; k<nFeatures; k++)
                {
                    double sum = 0;
                    for(int i=0; i<nSamples; i++) sum += X_std[i][j] * X_std[i][k];
                    cov[j,k] = sum / (nSamples - 1);
                }
            }

            // 3. Power Iteration to find dominant eigenvector
            double[] b_k = new double[nFeatures];
            for(int i=0; i<nFeatures; i++) b_k[i] = new Random().NextDouble(); // Random init

            for(int iter=0; iter<100; iter++)
            {
                // Matrix vector multiplication: b_k1 = Cov * b_k
                double[] b_k1 = new double[nFeatures];
                for(int r=0; r<nFeatures; r++)
                {
                    for(int c=0; c<nFeatures; c++)
                    {
                        b_k1[r] += cov[r,c] * b_k[c];
                    }
                }

                // Normalize
                double norm = Math.Sqrt(b_k1.Sum(v => v*v));
                for(int i=0; i<nFeatures; i++) b_k[i] = b_k1[i] / norm;
            }

            FirstComponent = b_k;
        }

        public double[] Transform(double[][] X)
        {
            // Projects data onto the first component
            return X.Select(row => MatrixMath.DotProduct(row, FirstComponent)).ToArray();
        }
    }

    // --- Hierarchical Clustering (Agglomerative) ---
    public class HierarchicalClustering
    {
        public int[] FitPredict(double[][] X, int nClusters)
        {
            int nSamples = X.Length;
            // Initially each point is a cluster
            List<List<int>> clusters = new List<List<int>>();
            for(int i=0; i<nSamples; i++) clusters.Add(new List<int> { i });

            while(clusters.Count > nClusters)
            {
                // Find closest pair of clusters (Single Linkage for simplicity)
                int c1 = -1, c2 = -1;
                double minDistance = double.MaxValue;

                for(int i=0; i<clusters.Count; i++)
                {
                    for(int j=i+1; j<clusters.Count; j++)
                    {
                        double dist = GetClusterDistance(X, clusters[i], clusters[j]);
                        if(dist < minDistance)
                        {
                            minDistance = dist;
                            c1 = i;
                            c2 = j;
                        }
                    }
                }

                // Merge c2 into c1
                clusters[c1].AddRange(clusters[c2]);
                clusters.RemoveAt(c2);
            }

            // Convert to labels
            int[] labels = new int[nSamples];
            for(int k=0; k<clusters.Count; k++)
            {
                foreach(int index in clusters[k]) labels[index] = k;
            }
            return labels;
        }

        private double GetClusterDistance(double[][] X, List<int> c1, List<int> c2)
        {
            // Centroid linkage: dist between centroids
            double[] centroid1 = ComputeCentroid(X, c1);
            double[] centroid2 = ComputeCentroid(X, c2);
            return MatrixMath.EuclideanDistance(centroid1, centroid2);
        }

        private double[] ComputeCentroid(double[][] X, List<int> indices)
        {
            int nFeat = X[0].Length;
            double[] c = new double[nFeat];
            foreach(int i in indices)
            {
                for(int j=0; j<nFeat; j++) c[j] += X[i][j];
            }
            for(int j=0; j<nFeat; j++) c[j] /= indices.Count;
            return c;
        }
    }
}
