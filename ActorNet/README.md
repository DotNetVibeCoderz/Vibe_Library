# ActorNet

ActorNet is a high-performance, hybrid actor framework for .NET that combines the best features of **Orleans** (Virtual Actors) and **Akka.NET** (Explicit Control). It provides a simple, scalable, and resilient platform for building distributed systems.

## Features

- **Virtual Actors (Grains)**: Automatically activated on first message, deactivated when idle. No manual lifecycle management needed.
- **Location Transparency**: Send messages to actors by ID, regardless of where they are located (local or remote).
- **High Throughput**: Uses `System.Threading.Channels` for efficient, lock-free message passing.
- **Networking**: Built-in TCP-based messaging for distributed nodes.
- **Persistence**: Pluggable state management (Simulated in this demo).
- **Beautiful UI**: Console dashboard powered by Spectre.Console.

## Getting Started

### Prerequisites
- .NET 8.0 SDK or later

### Installation

Clone the repository and build the project:
```bash
dotnet build
```

### Running the Demo

1. Navigate to the project folder.
2. Run the application:
   ```bash
   dotnet run
   ```
3. Use the interactive menu to:
   - Create Bank Accounts (Virtual Actors)
   - Deposit/Withdraw funds
   - Run a high-performance benchmark

### Usage Example

```csharp
// 1. Initialize the System
var system = new ActorSystem("Node1", 9000);
system.RegisterActorType<MyActor>();
system.Start();

// 2. Send a message (Actor is created automatically if not exists)
await system.SendMessageAsync("MyActor/user123", new MyMessage("Hello"));

// 3. Define an Actor
public class MyActor : VirtualActor
{
    public override async Task ReceiveAsync(IActorContext context, object message)
    {
        if (message is MyMessage m)
        {
            Console.WriteLine($"Received: {m.Content}");
        }
    }
}
```

---

# ActorNet (Bahasa Indonesia)

ActorNet adalah framework actor hybrid performa tinggi untuk .NET yang menggabungkan fitur terbaik dari **Orleans** (Virtual Actors) dan **Akka.NET** (Kontrol Eksplisit). Framework ini menyediakan platform yang sederhana, skalabel, dan tangguh untuk membangun sistem terdistribusi.

## Fitur Utama

- **Virtual Actors (Grains)**: Aktor diaktifkan secara otomatis saat menerima pesan pertama dan dinonaktifkan saat tidak aktif. Tidak perlu manajemen siklus hidup manual.
- **Transparansi Lokasi**: Kirim pesan ke aktor berdasarkan ID, tanpa mempedulikan lokasi fisik mereka (lokal atau remote).
- **Throughput Tinggi**: Menggunakan `System.Threading.Channels` untuk pengiriman pesan yang efisien dan minim locking.
- **Networking**: Mendukung pengiriman pesan antar node via TCP.
- **Persistensi**: Manajemen state yang dapat diganti (Disimulasikan dalam demo ini).
- **UI Menawan**: Dashboard konsol menggunakan Spectre.Console.

## Cara Menggunakan

### Prasyarat
- .NET 8.0 SDK atau yang lebih baru

### Instalasi

Clone repositori dan build project:
```bash
dotnet build
```

### Menjalankan Demo

1. Masuk ke folder project.
2. Jalankan aplikasi:
   ```bash
   dotnet run
   ```
3. Gunakan menu interaktif untuk:
   - Membuat Akun Bank (Virtual Actors)
   - Melakukan Setor/Tarik Tunai
   - Menjalankan benchmark performa tinggi

### Contoh Kode

```csharp
// 1. Inisialisasi Sistem
var system = new ActorSystem("Node1", 9000);
system.RegisterActorType<MyActor>();
system.Start();

// 2. Kirim pesan (Aktor dibuat otomatis jika belum ada)
await system.SendMessageAsync("MyActor/user123", new MyMessage("Halo"));

// 3. Definisi Aktor
public class MyActor : VirtualActor
{
    public override async Task ReceiveAsync(IActorContext context, object message)
    {
        if (message is MyMessage m)
        {
            Console.WriteLine($"Diterima: {m.Content}");
        }
    }
}
```
