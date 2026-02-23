# RLNet - Reinforcement Learning Library & Visualizer

**RLNet** adalah library Reinforcement Learning sederhana namun powerful yang dibangun dengan C# dan .NET 8.0. Project ini mencakup library inti (`RLNet.Core`) dan aplikasi visualisasi interaktif (`RLNet.Visualizer`) yang menggunakan Avalonia UI.

## Fitur Utama

### 1. Multi-Environment Simulation
RLNet mendukung berbagai jenis simulasi untuk menguji algoritma RL:
- **GridWorld**: Simulasi klasik pencarian jalur (Pathfinding) dengan Start, Goal, dan Jebakan (Traps).
- **CartPole**: Masalah kontrol klasik untuk menyeimbangkan tongkat di atas kereta bergerak (Balancing Problem).
- **LunarLander**: Simulasi pendaratan pesawat ruang angkasa dengan kontrol mesin utama dan samping (Physics Control).
- **Trading (Finance)**: Simulasi perdagangan saham sederhana dengan harga *Random Walk*, agen belajar kapan harus Buy, Sell, atau Hold.

### 2. Core Library (`RLNet.Core`)
- **Q-Learning Algorithm**: Implementasi *Tabular Q-Learning* yang efisien.
- **Continuous State Support**: Mendukung *State Discretization* untuk menangani input kontinu (seperti sudut tongkat atau kecepatan pesawat) agar bisa diproses oleh algoritma tabel.
- **Modular Design**: Mudah untuk menambahkan environment atau algoritma baru.

### 3. Visualizer (`RLNet.Visualizer`)
- **Cross-Platform**: Berjalan di Windows, Linux, dan macOS (berkat Avalonia UI).
- **Interactive Control**:
  - Ganti simulasi secara instan via Dropdown.
  - Atur kecepatan latihan (*Training Speed*) dengan Slider.
  - Tombol Start/Stop dan Reset.
- **Real-time Metrics**: Pantau *Episode*, *Step*, *Epsilon* (tingkat eksplorasi), dan *Total Reward* secara langsung.

## Cara Menjalankan

1. Pastikan **.NET 8.0 SDK** sudah terinstall di komputer Anda.
2. Buka terminal dan masuk ke direktori visualizer:
   ```bash
   cd RLNet.Visualizer
   ```
3. Jalankan aplikasi:
   ```bash
   dotnet run
   ```
4. Di aplikasi:
   - Pilih **Environment** (misal: CartPole) di panel kanan.
   - Klik tombol **Start Training**.
   - Perhatikan bagaimana agen belajar dari kesalahan (Trial & Error).

## Struktur Project

- **RLNet.Core**: Class Library yang berisi logika RL (Agent, Environments, State Discretizer).
- **RLNet.Visualizer**: Aplikasi Desktop (Avalonia) untuk antarmuka pengguna.

---

# RLNet - Reinforcement Learning Library & Visualizer (English)

**RLNet** is a simple yet powerful Reinforcement Learning library built with C# and .NET 8.0. It includes a core library (`RLNet.Core`) and an interactive visualization app (`RLNet.Visualizer`) built with Avalonia UI.

## Key Features

### 1. Multi-Environment Simulation
RLNet supports various simulations to test RL algorithms:
- **GridWorld**: Classic pathfinding simulation with Start, Goal, and Traps.
- **CartPole**: Classic control problem of balancing a pole on a cart.
- **LunarLander**: Spacecraft landing simulation with main and side engine controls.
- **Trading (Finance)**: Simple stock trading simulation with *Random Walk* prices, where the agent learns to Buy, Sell, or Hold.

### 2. Core Library (`RLNet.Core`)
- **Q-Learning Algorithm**: Efficient implementation of *Tabular Q-Learning*.
- **Continuous State Support**: Supports *State Discretization* to handle continuous inputs (like pole angle or velocity) for tabular algorithms.
- **Modular Design**: Easy to extend with new environments or algorithms.

### 3. Visualizer (`RLNet.Visualizer`)
- **Cross-Platform**: Runs on Windows, Linux, and macOS (via Avalonia UI).
- **Interactive Control**:
  - Switch simulations instantly via Dropdown.
  - Adjust *Training Speed* with a Slider.
  - Start/Stop and Reset buttons.
- **Real-time Metrics**: Monitor *Episode*, *Step*, *Epsilon* (exploration rate), and *Total Reward* live.

## How to Run

1. Ensure **.NET 8.0 SDK** is installed.
2. Open terminal and navigate to the visualizer directory:
   ```bash
   cd RLNet.Visualizer
   ```
3. Run the application:
   ```bash
   dotnet run
   ```
4. In the app:
   - Select an **Environment** (e.g., CartPole) from the right panel.
   - Click **Start Training**.
   - Watch the agent learn from trial and error.

## Project Structure

- **RLNet.Core**: Class Library containing RL logic (Agent, Environments, State Discretizer).
- **RLNet.Visualizer**: Desktop Application (Avalonia) for the user interface.

---
*Created by Jacky the Code Bender for Gravicode Studios.*