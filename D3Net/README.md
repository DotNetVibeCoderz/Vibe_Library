# D3Net - Cross-Platform Data Visualization Library

![D3Net Logo](https://img.shields.io/badge/D3Net-Visualization-blue)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-green)
![Framework](https://img.shields.io/badge/Framework-Avalonia-purple)

## 🎨 Overview

D3Net adalah library visualisasi data cross-platform yang terinspirasi dari D3.js, dibangun menggunakan **Avalonia UI Framework**. Library ini menyediakan 10 jenis chart yang berbeda dengan animasi smooth dan grafis yang menarik.

### ✨ Features

- ✅ **Cross-Platform**: Berjalan di Windows, Linux, dan macOS
- ✅ **10 Chart Types**: Berbagai jenis visualisasi untuk kebutuhan berbeda
- ✅ **Smooth Animations**: Animasi yang halus menggunakan Avalonia Animation API
- ✅ **Beautiful Graphics**: Desain modern dengan color palette yang menarik
- ✅ **Easy to Use**: API yang sederhana dan mudah dipahami
- ✅ **Responsive**: Menyesuaikan dengan ukuran window

## 📊 Supported Chart Types

### 1. 📊 Bar Chart
Grafik batang untuk membandingkan nilai-nilai kategori.
- Animasi cascade yang smooth
- Grid lines dan labels
- Color-coded bars

### 2. 📈 Line Chart
Grafik garis untuk menampilkan trend data.
- Multiple series support
- Animated line drawing
- Data points dengan hover effect

### 3. 🥧 Pie Chart
Grafik lingkaran untuk menampilkan proporsi data.
- Smooth slice animation
- Percentage labels
- Interactive legend

### 4. 📉 Area Chart
Grafik area dengan gradient fill.
- Gradient background
- Smooth line overlay
- Volume visualization

### 5. ⚫ Scatter Plot
Plot titik untuk menampilkan korelasi data.
- Multiple cluster support
- Color-coded groups
- Animated point appearance

### 6. 🔵 Bubble Chart
Grafik gelembung untuk 3-dimensional data.
- Size represents third dimension
- Floating animation
- Multi-variable visualization

### 7. 🕸️ Radar Chart
Grafik radar untuk analisis multi-dimensi.
- Spider web visualization
- Multiple series comparison
- Category-based analysis

### 8. 🔥 Heatmap
Peta panas untuk visualisasi matriks data.
- Color intensity mapping
- Grid-based layout
- Time-series patterns

### 9. 🍩 Donut Chart
Grafik donat dengan center information.
- Hollow center design
- Percentage display
- Category breakdown

### 10. 🎯 Gauge Chart
Speedometer-style gauge untuk monitoring.
- Real-time value display
- Status indicators
- Performance metrics

## 🚀 Getting Started

### Prerequisites

- .NET SDK 6.0 or higher
- Avalonia UI

### Installation

1. Clone repository ini
2. Restore NuGet packages:
```bash
dotnet restore
```

3. Build project:
```bash
dotnet build
```

4. Run aplikasi:
```bash
dotnet run
```

## 💻 Usage Example

```csharp
// Create a bar chart
var barChart = new BarChart();
barChart.Render(canvas);

// Create a line chart
var lineChart = new LineChart();
lineChart.Render(canvas);

// Create a pie chart
var pieChart = new PieChart();
pieChart.Render(canvas);
```

## 🎨 Customization

Setiap chart dapat dikustomisasi dengan:
- **Colors**: Modify `ColorPalette` di `BaseChart`
- **Animation**: Adjust timing dan easing functions
- **Data**: Provide custom data sources
- **Styling**: Modify stroke, fill, dan visual properties

## 🏗️ Architecture

```
D3Net/
├── Program.cs              # Entry point
├── App.axaml              # Application configuration
├── MainWindow.axaml       # Main UI layout
├── Charts/                # Chart implementations
│   ├── BaseChart.cs       # Base class untuk semua charts
│   ├── BarChart.cs        # Bar chart implementation
│   ├── LineChart.cs       # Line chart implementation
│   ├── PieChart.cs        # Pie chart implementation
│   ├── AreaChart.cs       # Area chart implementation
│   ├── ScatterChart.cs    # Scatter plot implementation
│   ├── BubbleChart.cs     # Bubble chart implementation
│   ├── RadarChart.cs      # Radar chart implementation
│   ├── Heatmap.cs         # Heatmap implementation
│   ├── DonutChart.cs      # Donut chart implementation
│   └── GaugeChart.cs      # Gauge chart implementation
└── README.md              # Documentation
```

## 🎯 Key Technologies

- **Avalonia UI**: Cross-platform XAML-based UI framework
- **.NET 6+**: Modern .NET platform
- **Avalonia Animations**: Built-in animation system
- **Vector Graphics**: Path-based rendering untuk quality tinggi

## 🌟 Animation System

D3Net menggunakan Avalonia Animation API dengan berbagai easing functions:
- `CubicEaseOut`: Smooth deceleration
- `ElasticEaseOut`: Bouncy spring effect
- `BackEaseOut`: Overshoot and settle
- `BounceEaseOut`: Multiple bounces
- `SineEaseInOut`: Smooth sine wave

## 📱 Platform Support

| Platform | Status | Notes |
|----------|--------|-------|
| Windows  | ✅ Supported | Full feature support |
| Linux    | ✅ Supported | Tested on Ubuntu 20.04+ |
| macOS    | ✅ Supported | Tested on macOS 11+ |

## 🤝 Contributing

Contributions are welcome! Feel free to:
- Report bugs
- Suggest new features
- Submit pull requests
- Improve documentation

## 📄 License

This project is created by **Gravicode Studios** team led by Kang Fadhil.

## 💝 Support

Kalau kamu suka project ini, traktir pulsa dong! 😊
Kirim ke: https://studios.gravicode.com/products/budax

## 🔗 Links

- Website: https://studios.gravicode.com
- Avalonia UI: https://avaloniaui.net
- D3.js (inspiration): https://d3js.org

## 📞 Contact

Created by **Jacky the Code Bender** 🚀
Gravicode Studios Team

---

**Happy Visualizing! 📊✨**
