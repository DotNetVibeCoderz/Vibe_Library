# MicroUI

**[Bahasa Indonesia]**

## Deskripsi
MicroUI adalah kerangka kerja (framework) antarmuka pengguna (UI) yang ringan dan lintas platform, dibangun menggunakan C# untuk ekosistem .NET. Proyek ini dirancang untuk mensimulasikan pengembangan UI pada sistem tertanam (embedded systems) atau aplikasi sederhana dengan ketergantungan minimal. MicroUI menggunakan **Avalonia UI** sebagai backend untuk merender grafis dan menangani input, yang memungkinkannya berjalan di Windows, Linux, dan macOS.

Framework ini mendefinisikan sistem kontrolnya sendiri (`MControl`) yang terpisah dari kontrol asli Avalonia, memberikan abstraksi sederhana yang mudah dipelajari dan digunakan.

## Fitur Utama
- **Ringan & Sederhana**: Struktur kontrol yang mudah dipahami (Panel, Button, Label, dll).
- **Dukungan Tata Letak XML**: Anda dapat mendesain antarmuka menggunakan file XML, mirip dengan XAML atau HTML.
- **Simulator Cross-Platform**: Berjalan di mana saja .NET dan Avalonia didukung.
- **Kontrol Bawaan Lengkap**: Termasuk Button, Label, TextBox, Gauge, Chart, Slider, ToggleSwitch, dan banyak lagi.
- **Sistem Tema**: Mendukung tema dasar (Metro, Material, Modern).
- **Penanganan Input**: Abstraksi input sentuh (Touch) dan mouse.
- **Animasi Dasar**: Dukungan untuk animasi properti sederhana.

## Struktur Proyek
- **MicroUI.Core**: Berisi logika inti framework, definisi kontrol (`MControl`), manajemen tema, dan struktur data dasar.
- **MicroUI.Services**: Layanan utilitas seperti `XmlLoader` untuk memuat layout dari file XML.
- **MicroUI.Simulator**: Implementasi host yang menggunakan Avalonia untuk menggambar kontrol MicroUI ke layar.
- **Layouts/**: Folder berisi contoh file layout XML.

## Cara Menjalankan
1. Pastikan Anda telah menginstal **.NET SDK** (versi terbaru yang kompatibel).
2. Buka terminal di direktori proyek.
3. Jalankan perintah:
   ```bash
   dotnet run
   ```
4. Jendela simulator akan muncul menampilkan antarmuka MicroUI.

## Cara Penggunaan
Anda dapat membuat antarmuka pengguna dengan dua cara:

### 1. Menggunakan Kode C#
```csharp
var panel = new MPanel();
var button = new MButton { Text = "Klik Saya", X = 50, Y = 50 };
button.OnClick += (s) => Console.WriteLine("Tombol diklik!");
panel.Add(button);
```

### 2. Menggunakan XML
Buat file XML (misal `Screen1.xml`):
```xml
<Panel Background="#FFFFFF">
    <Button Text="Klik Saya" X="50" Y="50" Width="100" Height="40" />
</Panel>
```
Lalu muat dalam kode:
```csharp
var root = XmlLoader.LoadFromFile("Layouts/Screen1.xml");
```

---

**[English]**

## Description
MicroUI is a lightweight, cross-platform User Interface (UI) framework built using C# for the .NET ecosystem. This project is designed to simulate UI development for embedded systems or simple applications with minimal dependencies. MicroUI uses **Avalonia UI** as a backend for rendering graphics and handling input, allowing it to run on Windows, Linux, and macOS.

The framework defines its own control system (`MControl`) separate from native Avalonia controls, providing a simple abstraction that is easy to learn and use.

## Key Features
- **Lightweight & Simple**: Easy-to-understand control structure (Panel, Button, Label, etc.).
- **XML Layout Support**: Design interfaces using XML files, similar to XAML or HTML.
- **Cross-Platform Simulator**: Runs anywhere .NET and Avalonia are supported.
- **Comprehensive Built-in Controls**: Includes Button, Label, TextBox, Gauge, Chart, Slider, ToggleSwitch, and more.
- **Theme System**: Supports basic themes (Metro, Material, Modern).
- **Input Handling**: Abstraction for touch and mouse input.
- **Basic Animation**: Support for simple property animations.

## Project Structure
- **MicroUI.Core**: Contains the core framework logic, control definitions (`MControl`), theme management, and basic data structures.
- **MicroUI.Services**: Utility services like `XmlLoader` for loading layouts from XML files.
- **MicroUI.Simulator**: Host implementation using Avalonia to draw MicroUI controls to the screen.
- **Layouts/**: Folder containing example XML layout files.

## How to Run
1. Ensure you have the **.NET SDK** (latest compatible version) installed.
2. Open a terminal in the project directory.
3. Run the command:
   ```bash
   dotnet run
   ```
4. The simulator window will appear displaying the MicroUI interface.

## How to Use
You can create user interfaces in two ways:

### 1. Using C# Code
```csharp
var panel = new MPanel();
var button = new MButton { Text = "Click Me", X = 50, Y = 50 };
button.OnClick += (s) => Console.WriteLine("Button clicked!");
panel.Add(button);
```

### 2. Using XML
Create an XML file (e.g., `Screen1.xml`):
```xml
<Panel Background="#FFFFFF">
    <Button Text="Click Me" X="50" Y="50" Width="100" Height="40" />
</Panel>
```
Then load it in code:
```csharp
var root = XmlLoader.LoadFromFile("Layouts/Screen1.xml");
```
