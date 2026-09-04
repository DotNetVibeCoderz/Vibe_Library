# Perkakas

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[English](../en/09-tooling.md) · [Indeks dokumentasi](README.md)*

Tiga permukaan di atas runtime yang sama: CLI terminal, konsol web, dan sample desktop.

## CLI

```bash
dotnet tool install -g ActorNet.Cli   # perintah "actornet"
```

| Perintah | |
| --- | --- |
| `run` | Jalankan sebuah node dan biarkan hidup |
| `monitor` | Jalankan node dengan dashboard langsung di terminal |
| `demo <skenario>` | Jalankan sebuah skenario |
| `cluster` | Bergabung ke cluster dan tampilkan keanggotaan serta penempatan key |
| `bench` | Ukur throughput |
| `scenarios` | Daftar skenario dan apa yang ditunjukkan masing-masing |

Opsi bersama: `--node-id`, `--host`, `-p|--port`, `--seed` (bisa berulang), `--cluster`, `--data`,
`--idle-timeout`, `-v|--verbose`.

`--data <dir>` menukar store memori dengan store berbasis file, dan itulah yang membuat "matikan,
jalankan lagi, saldonya masih ada" bisa diperagakan alih-alih sekadar diklaim.

Setel `ACTORNET_DEBUG=1` untuk stack trace lengkap. Secara bawaan sebuah kegagalan mencetak satu
baris — stack trace tepat untuk bug framework dan tidak tepat untuk "port 9000 sudah dipakai", yang
merupakan sebagian besar hal yang salah.

### Demo

```bash
actornet demo banking      # rekening event-sourced; 200 setoran bersamaan, tanpa lock
actornet demo telemetry    # stream reaktif ke satu actor per perangkat
actornet demo ordering     # saga yang melakukan kompensasi saat pembayaran gagal
actornet demo lifecycle    # aktivasi, deaktivasi, aktivasi ulang dari journal
actornet demo              # menu
```

Masing-masing mencetak apa yang akan dilakukannya, melakukannya, lalu menunjukkan angka yang
membuktikan itu berhasil.

### Monitor

```bash
actornet monitor --load --refresh 250 --top 20
```

Menggambar ulang tata letak tetap di tempat alih-alih menggulir — monitor yang menggulir sudah tidak
terbaca jauh di bawah kecepatan runtime ini. `--load` menghasilkan lalu lintas sintetis supaya ada
yang bisa dilihat.

### Bench

```bash
actornet bench -n 2000000 -a 8
```

Melaporkan dua angka dengan sengaja:

```
Dispatch only   3,035,763 msg/s  (yang dilaporkan benchmark naif)
Drained         2,981,276 msg/s  (setiap pesan ditangani)
```

Sebuah `tell` selesai saat pesan *diterima* ke mailbox, jadi mengukur perulangan `tell` mengukur
seberapa cepat proses ini mengisi channel. Angka "drained" dibatasi oleh penghalang berupa satu ask
per actor, yang terurut di belakang semua yang sudah mengantre — balasannya membuktikan antrean
terkuras. Lihat [Performa](10-performa.md).

## Konsol web

```bash
dotnet run --project src/ActorNet.Dashboard
```

Ia sendiri adalah sebuah node, jadi angka-angkanya adalah penghitung milik runtime, bukan hasil
scraping.

```bash
dotnet run --project src/ActorNet.Dashboard -- \
  --ActorNet:NodeId=console-1 \
  --ActorNet:Port=9100 \
  --ActorNet:Seeds:0=127.0.0.1:9000
```

Setel `ActorNet:GenerateLoad=false` untuk melihat beban kerja nyata alih-alih yang sintetis.

### Overview

![Overview konsol](../images/console-overview.png)

Throughput, pesan yang ditangani, sedang berjalan, actor aktif, kegagalan, restart — dan actor
tersibuk, karena pada node dengan ribuan actor yang menarik adalah yang sedang bekerja.

**In flight** menghitung pesan yang diterima untuk ditangani *di node ini*. Pesan yang diteruskan ke
node pemilik key-nya tidak sedang berjalan di sini. Salah menangani itu membuat konsol menampilkan
3.524 pesan berjalan melawan 26 actor aktif, dan itulah yang menangkap bug-nya.

### Actors

![Halaman actors](../images/console-actors.png)

Setiap aktivasi di node ini, bisa disaring, dengan tombol **Deactivate**.

Menonaktifkan bukan tindakan destruktif: ia menjalankan hook deaktivasi, tempat actor persisten
menuliskan state-nya, lalu menghapus actor itu dari node ini. Alamatnya tetap sah dan pesan berikutnya
mengaktifkan instance baru dari store — persis yang dilakukan penyapu menganggur.

### Cluster

![Halaman cluster](../images/console-cluster.png)

Tabel member, dan ring digambar apa adanya: satu busur per rentang ruang hash, diwarnai menurut node
pemiliknya. Garis-garisnya adalah virtual node, dan cara mereka berselang-seling itulah sebabnya
menambah satu member memindahkan kira-kira 1/N keyspace.

Ketik sebuah alamat ke dalam probe dan sebuah penanda mendarat di busur yang memilikinya — perhitungan
yang sama dengan yang dilakukan runtime pada setiap pengiriman.

### HTTP API

```bash
curl localhost:5170/api/metrics
curl localhost:5170/api/cluster
curl localhost:5170/api/deadletters
```

Hanya-baca, supaya angka yang sama tersedia untuk scrape atau skrip tanpa perlu mengeruk layar.

## Sample desktop

```bash
dotnet run --project samples/ActorNet.Samples.Avalonia
```

Empat skenario di atas satu node bersama, dijalankan sekali dan dipertahankan selama jendela hidup.
Masing-masing menyatakan sifat model actor yang ditunjukkannya, lalu membuktikannya.

**Banking** — 800 setoran bersamaan dari 16 task ke satu rekening. Sample-nya menyatakan saldo yang
diharapkan *sebelum* dijalankan, dan itu satu-satunya cara assertion semacam itu berarti.

![Banking](../images/samples-banking.png)

**Telemetry** — stream langsung ke satu actor per perangkat, dengan satu perangkat kelebihan suhu
untuk menguji jalur alarm. Tekan "Deactivate all devices" dan biarkan stream berjalan: mereka kembali
dengan hitungan yang utuh.

**Ordering** — saga lintas inventaris dan pembayaran. Setel batas kredit di bawah total sebuah pesanan
dan saga gagal di tahap pembayaran *setelah* stok direservasi; perhatikan angka "held" naik kembali.
"Try to oversell" menembakkan lebih banyak pesanan satuan daripada stok yang ada dan angkanya tidak
pernah menjadi negatif.

**Supervision** — empat actor dari class yang sama, didaftarkan dengan strategi berbeda, diberi
exception yang sama.

![Supervisi](../images/samples-supervision.png)

## Benchmark

```bash
dotnet run -c Release --project benchmarks/ActorNet.Benchmarks
dotnet run -c Release --project benchmarks/ActorNet.Benchmarks -- --filter '*RoutingBenchmarks*'
```

BenchmarkDotNet, dengan diagnostik memori. `MessagingBenchmarks` mencakup jalur pesan;
`RoutingBenchmarks` mencakup hashing, penempatan, dan serialisasi.

## Dead letter

Pesan yang tidak bisa dikirim dicatat, bukan sekadar di-log lalu dibuang. Sekadar mencatatnya di log
membuatnya tak terlihat oleh apa pun kecuali manusia yang membaca log.

```csharp
foreach (var letter in system.DeadLetters.Recent(20))
    Console.WriteLine($"{letter.Target} {letter.MessageType}: {letter.Reason} - {letter.Detail}");

system.DeadLetters.LetterRecorded += letter => alerting.Raise(letter);
```

| Alasan | |
| --- | --- |
| `UnregisteredActorType` | Alamatnya menyebut tipe yang tak pernah didaftarkan node ini |
| `UndeliverableToActor` | Actor-nya terus dinonaktifkan di antara pencarian dan pengiriman |
| `NodeUnreachable` | Node pemilik key tidak terjangkau |
| `UnknownMessageType` | Sebuah frame menyebut alias yang ditolak allow-list |
| `UnroutableFrame` | Alamat salah bentuk, atau frame tanpa payload |
| `Shutdown` | Node sedang berhenti |

Objek pesannya disimpan bila sudah sempat dimaterialisasi, sehingga sebuah letter bisa dikirim ulang
setelah penyebabnya diperbaiki. Ia sengaja **tidak ada** untuk frame yang ditolak sebelum
deserialisasi - mematerialisasi payload yang baru saja ditolak allow-list justru membatalkan
penolakan itu.

Buffer-nya berbatas dan membuang yang terlama; `Count` tetap total seumur hidup yang persis. Node yang
gagal mengirim biasanya gagal banyak, dan catatan tak berbatas atas itu adalah gangguan kedua di atas
yang pertama. Tukar lewat `options.DeadLetters`.

## Mengekspor ke OpenTelemetry

Konsol membaca penghitung milik runtime sendiri, yang cukup untuk satu node dan tidak berguna untuk
apa pun yang mengagregasi. Primitif standar .NET disediakan untuk itu:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(ActorNetDiagnostics.ActivitySourceName))
    .WithMetrics(m => m.AddMeter(ActorNetDiagnostics.MeterName));
```

ActorNet tidak mengambil dependensi ke OpenTelemetry untuk menyediakannya - keduanya hanya sebuah
`ActivitySource` dan sebuah `Meter`, jadi collector mana pun bisa mengambilnya.

| Instrumen | |
| --- | --- |
| `actornet.messages.processed` | Pesan yang ditangani, per tipe actor |
| `actornet.messages.failed` | Handler yang melempar exception, per tipe actor dan exception |
| `actornet.actors.activated` / `.deactivated` / `.restarted` | Siklus hidup, deaktivasi ditandai alasannya |
| `actornet.deadletters` | Yang tak terkirim, ditandai alasannya |
| `actornet.message.duration` | Waktu di dalam handler |
| `actornet.message.queue_time` | Waktu menunggu di mailbox |

Dari kedua histogram itu, **waktu antrean yang perlu diperhatikan**. Waktu handler menyatakan seberapa
mahal pekerjaannya; waktu antrean menyatakan apakah node-nya sanggup mengejar.

Setiap pesan yang ditangani juga menghasilkan span `"<TipeActor> receive"` berjenis `Consumer` -
sehingga penampil trace menggambarnya sebagai paruh penerima dari sebuah pengiriman. Handler yang
melempar exception menandai span-nya sebagai error dan melampirkan exception-nya.

Semua ini tidak berongkos apa pun saat tidak ada yang mendengarkan: `StartActivity` mengembalikan null
tanpa listener, dan instrumen tanpa collector tidak merekam. Itulah yang membuat tracing terjangkau di
jalur per-pesan.

## Selanjutnya

- [Performa](10-performa.md)
- [Pemecahan masalah](11-pemecahan-masalah.md)
