# KafkaNet

**KafkaNet** is a lightweight clone and simulation of Apache Kafka, written in C#. It demonstrates the core concepts of distributed streaming, including Producers, Consumers, Brokers, Topics, Partitions, and Stream Processing.

## Features

- **Distributed Architecture Simulation**: Simulates a Broker with multiple topics and partitions.
- **Persistence**: Messages are persisted to disk in an append-only log format.
- **High Throughput**: Capable of handling thousands of messages per second in simulation.
- **Stream Processing**: Built-in support for reactive stream processing using Rx.NET (similar to Kafka Streams).
- **ML Integration**: Includes a sample Anomaly Detection module using ML.NET.
- **Beautiful UI**: Interactive Console UI using Spectre.Console.

## Getting Started

### Prerequisites

- .NET 8.0 SDK or later

### Installation

1. Clone the repository.
2. Navigate to the project directory.
3. Run the application:

```bash
dotnet run
```

## Usage

### Main Menu

Upon running the application, you will see the following options:

1. **Produce Messages**: Send messages to a specific topic. You can specify the number of messages and key prefix.
2. **Consume Messages**: distinct consumer that reads messages from a topic in real-time.
3. **Stream Processing**: Demonstrates a reactive pipeline that filters high-value transactions (> $500).
4. **ML Anomaly Detection**: Trains a Logistic Regression model on the fly and predicts fraud in a real-time stream.
5. **Benchmark Throughput**: Measures the write performance of the broker.

### Code Example (API)

**Producer:**
```csharp
var broker = new Broker(1, "./kafka-data");
var producer = new ProducerClient(broker);
await producer.SendAsync("my-topic", "key-1", "Hello KafkaNet!");
```

**Consumer:**
```csharp
var consumer = new ConsumerClient(broker, "my-group");
consumer.Subscribe("my-topic");
await consumer.PollAsync(TimeSpan.FromSeconds(1), (topic, msg) => {
    Console.WriteLine($"Received: {msg.Value}");
});
```

---

# KafkaNet (Bahasa Indonesia)

**KafkaNet** adalah kloning ringan dan simulasi dari Apache Kafka, ditulis dalam C#. Proyek ini mendemonstrasikan konsep inti dari pemrosesan stream terdistribusi, termasuk Producer, Consumer, Broker, Topik, Partisi, dan Pemrosesan Stream.

## Fitur

- **Simulasi Arsitektur Terdistribusi**: Mensimulasikan Broker dengan banyak topik dan partisi.
- **Persistensi**: Pesan disimpan ke disk dalam format append-only log.
- **Throughput Tinggi**: Mampu menangani ribuan pesan per detik dalam simulasi.
- **Pemrosesan Stream**: Dukungan bawaan untuk pemrosesan stream reaktif menggunakan Rx.NET (mirip dengan Kafka Streams).
- **Integrasi ML**: Termasuk contoh Deteksi Anomali menggunakan ML.NET.
- **UI Menarik**: UI Konsol interaktif menggunakan Spectre.Console.

## Memulai

### Prasyarat

- .NET 8.0 SDK atau yang lebih baru

### Cara Menjalankan

1. Clone repositori ini.
2. Masuk ke direktori proyek.
3. Jalankan aplikasi:

```bash
dotnet run
```

## Panduan Penggunaan

### Menu Utama

Setelah menjalankan aplikasi, Anda akan melihat pilihan berikut:

1. **Produce Messages**: Mengirim pesan ke topik tertentu.
2. **Consume Messages**: Membaca pesan dari topik secara real-time.
3. **Stream Processing**: Mendemonstrasikan pipeline reaktif yang memfilter transaksi bernilai tinggi (> $500).
4. **ML Anomaly Detection**: Melatih model Regresi Logistik secara otomatis dan memprediksi penipuan dalam stream real-time.
5. **Benchmark Throughput**: Mengukur performa penulisan broker.

### Contoh Kode (API)

**Producer:**
```csharp
var broker = new Broker(1, "./kafka-data");
var producer = new ProducerClient(broker);
await producer.SendAsync("my-topic", "key-1", "Halo KafkaNet!");
```

**Consumer:**
```csharp
var consumer = new ConsumerClient(broker, "my-group");
consumer.Subscribe("my-topic");
await consumer.PollAsync(TimeSpan.FromSeconds(1), (topic, msg) => {
    Console.WriteLine($"Diterima: {msg.Value}");
});
```
