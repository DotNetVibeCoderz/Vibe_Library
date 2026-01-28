# ClassicML

**ClassicML** is a lightweight Machine Learning library written entirely in **C# from scratch**.
It does not rely on external mathematics libraries (like Math.NET or Python bindings), making it perfect for educational purposes to understand the underlying mathematics and logic of classic algorithms.

---

## 🇬🇧 English

### Features & Algorithms implemented
The following algorithms are implemented manually:

#### 1. Supervised Learning
*   **Linear Regression**: Predicts continuous values (e.g., house prices).
*   **Logistic Regression**: Binary classification using Sigmoid function.
*   **k-Nearest Neighbors (k-NN)**: Classification based on distance to nearest points.
*   **Naive Bayes (Gaussian)**: Probabilistic classifier based on Bayes' theorem.
*   **Linear SVM**: Support Vector Machine using Gradient Descent.
*   **Decision Tree**: Simplified CART algorithm for classification.
*   **Random Forest**: Ensemble of Decision Trees.

#### 2. Unsupervised Learning
*   **k-Means Clustering**: Partitions data into *k* clusters based on centroids.
*   **Principal Component Analysis (PCA)**: Dimensionality reduction using Power Iteration method.
*   **Hierarchical Clustering**: Agglomerative clustering (bottom-up).

#### 3. Neural Approaches
*   **Perceptron**: Single-layer neural network for linearly separable data.
*   **Multilayer Perceptron (MLP)**: Deep Feedforward Neural Network with **Backpropagation** (can solve XOR).

#### 4. Reinforcement Learning
*   **Multi-Armed Bandit**: Epsilon-Greedy strategy.
*   **Q-Learning / SARSA**: Tabular reinforcement learning for grid environments.

### Project Structure
*   `Algorithms/`: Contains the core logic for all models.
*   `Helpers/`: Contains `MatrixMath.cs` for manual vector/matrix operations.
*   `Program.cs`: Console application demonstrating usage with dummy data.

### How to Run
Ensure you have the .NET SDK installed.
```bash
dotnet build
dotnet run
```

---

## 🇮🇩 Bahasa Indonesia

### Fitur & Algoritma
**ClassicML** adalah library Machine Learning yang ditulis dari nol menggunakan C#. Tujuan utamanya adalah edukasi, untuk memahami bagaimana matematika di balik AI bekerja tanpa "magic" dari library pihak ketiga.

Berikut adalah algoritma yang sudah diimplementasikan:

#### 1. Supervised Learning (Pembelajaran Terawasi)
*   **Linear Regression**: Memprediksi nilai kontinu (contoh: harga rumah).
*   **Logistic Regression**: Klasifikasi biner menggunakan fungsi Sigmoid.
*   **k-Nearest Neighbors (k-NN)**: Klasifikasi berdasarkan jarak titik terdekat.
*   **Naive Bayes (Gaussian)**: Klasifikasi probabilistik berbasis teorema Bayes.
*   **Linear SVM**: Support Vector Machine menggunakan Gradient Descent.
*   **Decision Tree**: Algoritma pohon keputusan sederhana untuk klasifikasi.
*   **Random Forest**: Kumpulan (ensemble) dari beberapa Decision Tree.

#### 2. Unsupervised Learning (Pembelajaran Tak Terawasi)
*   **k-Means Clustering**: Mengelompokkan data menjadi *k* klaster.
*   **Principal Component Analysis (PCA)**: Reduksi dimensi data (misal: 2D ke 1D).
*   **Hierarchical Clustering**: Pengelompokan data secara bertingkat (agglomerative).

#### 3. Neural Approaches (Pendekatan Saraf Tiruan)
*   **Perceptron**: Model jaringan saraf paling sederhana.
*   **Multilayer Perceptron (MLP)**: Jaringan saraf tiruan dengan **Backpropagation** (bisa menyelesaikan masalah non-linear seperti XOR).

#### 4. Reinforcement Learning (Pembelajaran Penguatan)
*   **Multi-Armed Bandit**: Strategi Epsilon-Greedy untuk memilih opsi terbaik.
*   **Q-Learning / SARSA**: Pembelajaran berbasis tabel (Tabular RL) untuk navigasi sederhana.

### Struktur Project
*   `Algorithms/`: Berisi logika utama semua model ML.
*   `Helpers/`: Berisi `MatrixMath.cs` untuk operasi vektor/matriks manual.
*   `Program.cs`: Aplikasi Console yang mendemokan penggunaan setiap algoritma.

### Cara Menjalankan
Pastikan .NET SDK sudah terinstall di komputermu.
```bash
dotnet build
dotnet run
```

---
*Created by **Jacky the Code Bender** (Gravicode Studios)*
