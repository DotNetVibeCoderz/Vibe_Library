# KafkaNet

KafkaNet is a lightweight, educational clone of Apache Kafka written in C# (.NET). It demonstrates the core concepts of a distributed streaming platform, including topics, partitions, producers, consumers, and stream processing.

## Features

- **Topic & Partition Management**: Create topics with multiple partitions.
- **Persistence**: Messages are persisted to disk using an append-only log with CRC32 checksums for fault tolerance.
- **Network Support**: Client-Server architecture using TCP.
- **Producer & Consumer API**: Simple Async API for sending and receiving messages.
- **Stream Processing**: built-in support for reactive stream processing (Rx.NET).
- **ML Integration**: Example of real-time anomaly detection using ML.NET.
- **Fault Tolerance**: Data integrity verification and recovery from corruption.

## Architecture

- **Broker (Server)**: Manages topics and partitions, handles client requests via TCP (default port 9092).
- **Client**: Connects to the broker to produce or consume messages.
- **Protocol**: Custom binary/JSON protocol over TCP.

## Getting Started

### Prerequisites

- .NET SDK (6.0 or later recommended)

### Running the Project

1. Build the project:
   ```bash
   dotnet build
   ```

2. Run the application:
   ```bash
   dotnet run --project KafkaNet
   ```

3. Choose **Run as Server** in one terminal window.
   - This starts the broker on port 9092.

4. Run the application again in another terminal window and choose **Run as Client**.
   - Enter the server host (default: 127.0.0.1) and port (default: 9092).
   - Use the interactive menu to produce/consume messages.

## Usage Examples

Below are simple code snippets to get you started with KafkaNet Client API.

### 1. Producing Messages
Use `KafkaNetProducer` to send messages to a specific topic.

```csharp
using KafkaNet.Client;

// Connect to broker
var producer = new KafkaNetProducer("127.0.0.1", 9092);
await producer.ConnectAsync();

// Send a message to "my-topic"
string topic = "my-topic";
string message = "Hello KafkaNet!";
await producer.ProduceAsync(topic, message);

Console.WriteLine("Message sent!");
producer.Disconnect();
```

### 2. Consuming Messages
Use `KafkaNetConsumer` to read messages from a topic starting from a specific offset.

```csharp
using KafkaNet.Client;

// Connect to broker
var consumer = new KafkaNetConsumer("127.0.0.1", 9092);
await consumer.ConnectAsync();

// Consume messages from "my-topic" partition 0, starting at offset 0
string topic = "my-topic";
int partition = 0;
long offset = 0;

var messages = await consumer.ConsumeAsync(topic, partition, offset);

foreach (var msg in messages)
{
    Console.WriteLine($"Received: {msg.Value} (Offset: {msg.Offset})");
}

consumer.Disconnect();
```

### 3. Stream Processing
Use `StreamProcessor` to filter and transform data streams in real-time.

```csharp
using KafkaNet.Streams;

// Create a stream processor
var processor = new StreamProcessor();

// Define a source stream from a topic
var sourceStream = processor.Stream<string>("transactions");

// Process the stream: Filter high-value transactions
sourceStream
    .Where(msg => double.Parse(msg) > 500)
    .Subscribe(msg => Console.WriteLine($"High Value Transaction: {msg}"));

// Start processing (in a real app, this would run continuously)
```

## Demo Scenarios

1. **Basic Messaging**: Use "Produce Messages" to send data, then "Consume Messages" to read it back.
2. **Stream Processing**: Run "Stream Processing" to filter high-value transactions (> $500) in real-time.
3. **ML Anomaly Detection**: Uses a trained ML.NET model to detect fraud in a stream of transactions.
4. **Benchmark**: Test the throughput of the system.

## Project Structure

- `Core/`: Broker, Topic, Partition logic.
- `Network/`: TCP Server, Client, and Protocol definitions.
- `Client/`: High-level Producer and Consumer implementations.
- `Streams/`: Stream processing logic.
- `Program.cs`: CLI entry point.

## Fault Tolerance
The system uses `log.dat` files in `kafka-data/` to persist messages. Each message is written with a CRC32 checksum. On startup, the broker verifies the integrity of the log and truncates any corrupted data at the end of the file (e.g., from a power failure).

---

# KafkaNet (Bahasa Indonesia)

KafkaNet adalah tiruan ringan dan edukatif dari Apache Kafka yang ditulis dalam C# (.NET). Proyek ini mendemonstrasikan konsep inti dari platform streaming terdistribusi, termasuk topik, partisi, produsen (producer), konsumen (consumer), dan pemrosesan stream.

## Fitur

- **Manajemen Topik & Partisi**: Membuat topik dengan banyak partisi.
- **Persistensi**: Pesan disimpan ke disk menggunakan log append-only dengan checksum CRC32 untuk toleransi kesalahan.
- **Dukungan Jaringan**: Arsitektur Client-Server menggunakan TCP.
- **API Producer & Consumer**: API Asinkron sederhana untuk mengirim dan menerima pesan.
- **Pemrosesan Stream**: Dukungan bawaan untuk pemrosesan stream reaktif (Rx.NET).
- **Integrasi ML**: Contoh deteksi anomali real-time menggunakan ML.NET.
- **Toleransi Kesalahan**: Verifikasi integritas data dan pemulihan dari korupsi data.

## Arsitektur

- **Broker (Server)**: Mengelola topik dan partisi, menangani permintaan klien melalui TCP (port default 9092).
- **Client**: Terhubung ke broker untuk memproduksi atau mengonsumsi pesan.
- **Protokol**: Protokol biner/JSON kustom melalui TCP.

## Cara Memulai

### Prasyarat

- .NET SDK (6.0 atau yang lebih baru disarankan)

### Menjalankan Proyek

1. Build proyek:
   ```bash
   dotnet build
   ```

2. Jalankan aplikasi:
   ```bash
   dotnet run --project KafkaNet
   ```

3. Pilih **Run as Server** di satu jendela terminal.
   - Ini akan memulai broker pada port 9092.

4. Jalankan aplikasi lagi di jendela terminal lain dan pilih **Run as Client**.
   - Masukkan host server (default: 127.0.0.1) dan port (default: 9092).
   - Gunakan menu interaktif untuk memproduksi/mengonsumsi pesan.

## Contoh Penggunaan (Code Samples)

Berikut adalah potongan kode sederhana untuk memulai menggunakan KafkaNet Client API.

### 1. Mengirim Pesan (Producing Messages)
Gunakan `KafkaNetProducer` untuk mengirim pesan ke topik tertentu.

```csharp
using KafkaNet.Client;

// Terhubung ke broker
var producer = new KafkaNetProducer("127.0.0.1", 9092);
await producer.ConnectAsync();

// Kirim pesan ke "my-topic"
string topic = "my-topic";
string message = "Halo KafkaNet!";
await producer.ProduceAsync(topic, message);

Console.WriteLine("Pesan terkirim!");
producer.Disconnect();
```

### 2. Menerima Pesan (Consuming Messages)
Gunakan `KafkaNetConsumer` untuk membaca pesan dari topik mulai dari offset tertentu.

```csharp
using KafkaNet.Client;

// Terhubung ke broker
var consumer = new KafkaNetConsumer("127.0.0.1", 9092);
await consumer.ConnectAsync();

// Baca pesan dari "my-topic" partisi 0, mulai dari offset 0
string topic = "my-topic";
int partition = 0;
long offset = 0;

var messages = await consumer.ConsumeAsync(topic, partition, offset);

foreach (var msg in messages)
{
    Console.WriteLine($"Diterima: {msg.Value} (Offset: {msg.Offset})");
}

consumer.Disconnect();
```

### 3. Pemrosesan Stream (Stream Processing)
Gunakan `StreamProcessor` untuk memfilter dan mengubah aliran data secara real-time.

```csharp
using KafkaNet.Streams;

// Buat pemroses stream
var processor = new StreamProcessor();

// Definisikan sumber stream dari topik
var sourceStream = processor.Stream<string>("transactions");

// Proses stream: Filter transaksi bernilai tinggi
sourceStream
    .Where(msg => double.Parse(msg) > 500)
    .Subscribe(msg => Console.WriteLine($"Transaksi Bernilai Tinggi: {msg}"));

// Mulai pemrosesan (dalam aplikasi nyata, ini akan berjalan terus menerus)
```

## Skenario Demo

1. **Pesan Dasar**: Gunakan "Produce Messages" untuk mengirim data, lalu "Consume Messages" untuk membacanya kembali.
2. **Pemrosesan Stream**: Jalankan "Stream Processing" untuk memfilter transaksi bernilai tinggi (> $500) secara real-time.
3. **Deteksi Anomali ML**: Menggunakan model ML.NET yang terlatih untuk mendeteksi penipuan dalam aliran transaksi.
4. **Benchmark**: Uji throughput sistem.

## Struktur Proyek

- `Core/`: Logika Broker, Topik, Partisi.
- `Network/`: Server TCP, Client, dan definisi Protokol.
- `Client/`: Implementasi Producer dan Consumer tingkat tinggi.
- `Streams/`: Logika pemrosesan stream.
- `Program.cs`: Titik masuk CLI.

## Toleransi Kesalahan
Sistem menggunakan file `log.dat` di `kafka-data/` untuk menyimpan pesan. Setiap pesan ditulis dengan checksum CRC32. Saat startup, broker memverifikasi integritas log dan memotong (truncate) data yang rusak di akhir file (misalnya, akibat mati listrik).
