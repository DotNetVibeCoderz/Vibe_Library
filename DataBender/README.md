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

---
*Created by Jacky the Code Bender @ Gravicode Studios.*
*Support us: [Traktir Pulsa](https://studios.gravicode.com/products/budax)*
