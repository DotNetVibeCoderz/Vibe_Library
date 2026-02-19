# DataBender 🐼

**DataBender** is a high-performance Pandas clone for .NET (C#). It provides powerful data manipulation, analysis, and cleaning tools with a syntax familiar to Python data scientists.

## Features
- **Series & DataFrame**: Core 1D and 2D data structures.
- **Fast Indexing**: Support for labeled and positional indexing.
- **I/O**: Read/Write CSV (Extensible to JSON/Parquet).
- **Data Cleaning**: Handle missing values (`NaN`), filtering, and normalization.
- **Analytics**: Built-in mean, sum, max, min, and descriptive statistics.
- **Visualization**: Console-based ASCII charts.

## Quick Start
```csharp
var df = new DataFrame();
df.AddColumn("Product", new[] { "CPU", "GPU", "RAM" });
df.AddColumn("Price", new[] { 300, 500, 100 });
df.Print();
```

## Advanced Examples

### 1. Series Operations
```csharp
var s = new Series<int>(new[] { 10, 20, 30, 40 }, "MySeries");
Console.WriteLine($"Mean: {s.Mean()}, Sum: {s.Sum()}, Max: {s.Max()}");
```

### 2. Filtering Data
```csharp
// Filter rows where Age > 24
var filtered = df.Filter(i => Convert.ToInt32(df["Age"][i]) > 24);
filtered.Print();
```

### 3. Handling Missing Data (FillNa)
```csharp
var sWithNulls = new Series<double?>(new double?[] { 1.0, null, 3.5, null }, "NullableSeries");
var filled = sWithNulls.FillNa(0.0);
```

### 4. Visualization (ASCII Charts)
```csharp
// Bar Chart
Visualizer.BarChart(df, "Product", "Price");

// Pie Chart
Visualizer.PieChart(df, "Dept", "StaffCount");
```

---

# DataBender (Bahasa Indonesia) 🐼

**DataBender** adalah kloningan library Pandas berkinerja tinggi untuk .NET (C#). Menyediakan alat manipulasi, analisis, dan pembersihan data yang kuat dengan sintaks yang familiar bagi para data scientist Python.

## Fitur Utama
- **Series & DataFrame**: Struktur data 1D dan 2D yang fleksibel.
- **Indexing Cepat**: Mendukung indeks berlabel dan posisi.
- **I/O**: Baca/Tulis CSV.
- **Pembersihan Data**: Menangani nilai yang hilang (`NaN`), pemfilteran, dan normalisasi.
- **Analitik**: Statistik deskriptif bawaan seperti rata-rata, jumlah, maks, dan min.
- **Visualisasi**: Grafik ASCII berbasis konsol.

## Contoh Penggunaan
```csharp
var df = new DataFrame();
df.AddColumn("Nama", new[] { "Andi", "Budi", "Caca" });
df.AddColumn("Nilai", new[] { 85, 90, 78 });
df.Print();
```

## Contoh Lanjutan

### 1. Operasi Series
```csharp
var s = new Series<int>(new[] { 10, 20, 30, 40 }, "NilaiSaya");
Console.WriteLine($"Rata-rata: {s.Mean()}, Total: {s.Sum()}, Maks: {s.Max()}");
```

### 2. Filter Data
```csharp
// Filter baris di mana Umur > 24
var filtered = df.Filter(i => Convert.ToInt32(df["Umur"][i]) > 24);
filtered.Print();
```

### 3. Menangani Data Kosong (FillNa)
```csharp
var sWithNulls = new Series<double?>(new double?[] { 1.0, null, 3.5, null }, "SeriesKosong");
var filled = sWithNulls.FillNa(0.0); // Mengisi null dengan 0.0
```

### 4. Visualisasi (Grafik ASCII)
```csharp
// Bar Chart
Visualizer.BarChart(df, "Produk", "Harga");

// Pie Chart (Distribusi)
Visualizer.PieChart(df, "Departemen", "JumlahStaf");

// Scatter Plot
Visualizer.ScatterPlot(df, "Bulan", "Pendapatan");
```

---
*Dibuat oleh Jacky the Code Bender @ Gravicode Studios.*
*Dukung kami: [Traktir Pulsa](https://studios.gravicode.com/products/budax)*
