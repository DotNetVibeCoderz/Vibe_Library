# 🚀 Cara Menjalankan D3Net

## Prerequisites
- .NET SDK 6.0 atau lebih tinggi
- Windows, Linux, atau macOS

## Langkah-langkah Menjalankan

### 1. Restore Dependencies
```bash
cd D3Net
dotnet restore
```

### 2. Build Project
```bash
dotnet build
```

### 3. Run Aplikasi
```bash
dotnet run
```

## Struktur Project

```
D3Net/
├── Program.cs              # Entry point aplikasi
├── App.axaml              # Konfigurasi aplikasi Avalonia
├── App.axaml.cs           # Code-behind untuk App
├── MainWindow.axaml       # UI layout utama
├── MainWindow.axaml.cs    # Code-behind dengan event handlers
├── Charts/                # Folder berisi semua chart implementations
│   ├── BaseChart.cs       # Base class untuk semua charts
│   ├── AnimationHelper.cs # Helper untuk animasi
│   ├── BarChart.cs        # Bar Chart implementation
│   ├── LineChart.cs       # Line Chart implementation
│   ├── PieChart.cs        # Pie Chart implementation
│   ├── AreaChart.cs       # Area Chart implementation
│   ├── ScatterChart.cs    # Scatter Plot implementation
│   ├── BubbleChart.cs     # Bubble Chart implementation
│   ├── RadarChart.cs      # Radar Chart implementation
│   ├── Heatmap.cs         # Heatmap implementation
│   ├── DonutChart.cs      # Donut Chart implementation
│   └── GaugeChart.cs      # Gauge Chart implementation
├── README.md              # Dokumentasi lengkap
└── HOW_TO_RUN.md         # Panduan ini

```

## Fitur Aplikasi

### 10 Jenis Visualisasi Data:

1. **📊 Bar Chart** - Grafik batang untuk perbandingan data
2. **📈 Line Chart** - Grafik garis untuk trend data
3. **🥧 Pie Chart** - Grafik lingkaran untuk proporsi
4. **📉 Area Chart** - Grafik area dengan gradient
5. **⚫ Scatter Plot** - Plot titik untuk korelasi
6. **🔵 Bubble Chart** - Grafik gelembung 3D
7. **🕸️ Radar Chart** - Grafik radar multi-dimensi
8. **🔥 Heatmap** - Peta panas untuk matriks data
9. **🍩 Donut Chart** - Grafik donat dengan info center
10. **🎯 Gauge Chart** - Speedometer gauge untuk monitoring

## Cara Menggunakan

1. Jalankan aplikasi
2. Klik tombol di sidebar kiri untuk memilih jenis chart
3. Chart akan muncul di area canvas sebelah kanan
4. Setiap chart memiliki animasi smooth saat pertama kali ditampilkan
5. Data di-generate secara random setiap kali chart dibuat

## Teknologi yang Digunakan

- **Avalonia UI 11.x** - Cross-platform XAML UI framework
- **.NET 9.0** - Modern .NET platform
- **C# 12** - Latest C# language features
- **Vector Graphics** - Path-based rendering untuk kualitas tinggi

## Cross-Platform Support

✅ **Windows** - Fully supported
✅ **Linux** - Fully supported  
✅ **macOS** - Fully supported

## Troubleshooting

### Error: "Unable to find SDK"
Pastikan .NET SDK sudah terinstall dengan benar:
```bash
dotnet --version
```

### Aplikasi tidak muncul
Coba jalankan dengan verbose logging:
```bash
dotnet run --verbosity detailed
```

### Build warnings tentang async
Warning `CS4014` tentang unawaited async calls adalah normal dan disengaja untuk animasi fire-and-forget.

## Customization

### Mengubah Warna
Edit `ColorPalette` di `Charts/BaseChart.cs`:
```csharp
protected static readonly List<Color> ColorPalette = new List<Color>
{
    Color.FromRgb(52, 152, 219),   // Blue
    Color.FromRgb(46, 204, 113),   // Green
    // Tambahkan warna lain...
};
```

### Menambah Chart Baru
1. Buat class baru yang inherit dari `BaseChart`
2. Override method `Render(Canvas canvas)`
3. Tambahkan button di `MainWindow.axaml`
4. Tambahkan event handler di `MainWindow.axaml.cs`

### Mengubah Animasi
Edit parameter di `AnimationHelper.FadeIn()`:
```csharp
// Syntax: FadeIn(control, delayMs, durationMs)
AnimationHelper.FadeIn(element, 100, 600);
```

## Kontribusi

Project ini dibuat oleh **Gravicode Studios** team led by Kang Fadhil.
Dibuat oleh **Jacky the Code Bender** 🚀

Kalau suka, traktir pulsa dong! 😊
https://studios.gravicode.com/products/budax

## License

Free to use and modify. Attribution appreciated!

## Support

Untuk pertanyaan atau support:
- Website: https://studios.gravicode.com
- Avalonia Docs: https://docs.avaloniaui.net

---

**Selamat mencoba! Happy Visualizing! 📊✨**
