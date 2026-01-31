# NetScheduler

**[Bahasa Indonesia]**

## Deskripsi
NetScheduler adalah sebuah proyek perpustakaan scheduler berbasis .NET Console Application yang dirancang untuk mendemonstrasikan kemampuan penjadwalan tugas tingkat lanjut (enterprise-grade). Proyek ini dibangun di atas **Quartz.NET**, standar industri untuk penjadwalan di ekosistem .NET, dan dikonfigurasi menggunakan **Microsoft.Extensions.Hosting** untuk integrasi Dependency Injection yang mulus.

Proyek ini dibuat oleh **Jacky the code bender** dari **Gravicode Studios**.

## Fitur Utama
1.  **Job Scheduling & Execution**: Mendukung interval sederhana, ekspresi Cron, dan parameter dinamis.
2.  **Persistence**: Mendukung stateful job yang dapat menyimpan data antar eksekusi.
3.  **Concurrency Control**: Mencegah eksekusi tumpang tindih untuk job yang berjalan lama.
4.  **Error Handling**: Penanganan kesalahan dan simulasi kegagalan job.
5.  **Integration**: Integrasi penuh dengan .NET Core Dependency Injection (DI).
6.  **Misfire Handling**: Strategi penanganan job yang terlewat (misal: aplikasi mati).
7.  **Monitoring**: Menggunakan Listeners untuk logging dan audit eksekusi job.

## Prasyarat
- .NET SDK (versi terbaru disarankan)

## Cara Menjalankan
1.  Clone atau unduh repositori ini.
2.  Buka terminal di direktori proyek.
3.  Jalankan perintah:
    ```bash
    dotnet run
    ```
4.  Aplikasi akan berjalan dan menampilkan log aktivitas scheduler di console.

## 10 Skenario Penggunaan (Use Cases)
Kode demonstrasi dapat ditemukan di `SchedulerDemo.cs`:
1.  **Simple Interval Job**: Job berjalan setiap 5 detik.
2.  **Cron Job**: Job berjalan berdasarkan ekspresi Cron (setiap menit).
3.  **Job with Parameters**: Mengirim data ("Message", "Count") ke dalam job.
4.  **Stateful Job**: Job yang mengingat jumlah eksekusi sebelumnya (`[PersistJobDataAfterExecution]`).
5.  **Concurrency Control**: Job yang mencegah eksekusi paralel (`[DisallowConcurrentExecution]`).
6.  **Error Handling**: Simulasi job yang gagal (throw exception).
7.  **Dependency Injection**: Job yang menggunakan service lain (`IMyService`) via DI.
8.  **Misfire Handling**: Konfigurasi strategi jika jadwal terlewat.
9.  **Calendar Scheduling**: Pengecualian waktu tertentu (libur/blocked time).
10. **Global Listeners**: Memantau event sebelum dan sesudah job berjalan.

---

**[English]**

## Description
NetScheduler is a .NET Console Application based scheduler library project designed to demonstrate enterprise-grade task scheduling capabilities. It is built on top of **Quartz.NET**, the industry standard for scheduling in the .NET ecosystem, and configured using **Microsoft.Extensions.Hosting** for seamless Dependency Injection integration.

This project was crafted by **Jacky the code bender** from **Gravicode Studios**.

## Key Features
1.  **Job Scheduling & Execution**: Supports simple intervals, Cron expressions, and dynamic parameters.
2.  **Persistence**: Supports stateful jobs that persist data between executions.
3.  **Concurrency Control**: Prevents overlapping execution for long-running jobs.
4.  **Error Handling**: Error handling and job failure simulation.
5.  **Integration**: Full integration with .NET Core Dependency Injection (DI).
6.  **Misfire Handling**: Strategies for handling missed jobs (e.g., application downtime).
7.  **Monitoring**: Uses Listeners for job execution logging and auditing.

## Prerequisites
- .NET SDK (latest version recommended)

## How to Run
1.  Clone or download this repository.
2.  Open a terminal in the project directory.
3.  Run the command:
    ```bash
    dotnet run
    ```
4.  The application will start and display scheduler activity logs in the console.

## 10 Use Cases Implemented
Demonstration code can be found in `SchedulerDemo.cs`:
1.  **Simple Interval Job**: Runs every 5 seconds.
2.  **Cron Job**: Runs based on a Cron expression (every minute).
3.  **Job with Parameters**: Passes data ("Message", "Count") into the job.
4.  **Stateful Job**: Remembers state from previous executions (`[PersistJobDataAfterExecution]`).
5.  **Concurrency Control**: Prevents parallel execution (`[DisallowConcurrentExecution]`).
6.  **Error Handling**: Simulates a failing job (throws exception).
7.  **Dependency Injection**: Injects a service (`IMyService`) into the job via DI.
8.  **Misfire Handling**: Configured strategy for missed schedules.
9.  **Calendar Scheduling**: Excluding specific times (holidays/blocked time).
10. **Global Listeners**: Monitoring events before and after job execution.
