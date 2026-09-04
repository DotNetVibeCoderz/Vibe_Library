# Performa

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[English](../en/10-performance.md) · [Indeks dokumentasi](README.md)*

Setiap angka di sini diukur pada mesin yang dijelaskan di bawah. Tidak ada yang diekstrapolasi, dan
bagian akhir menyatakan terus terang apa yang **tidak** ditunjukkan angka-angka ini.

## Mesinnya

```
Intel Core i7-8650U, 1,90 GHz (Kaby Lake R) — 4 core fisik, 8 logis
Windows 11 (10.0.26200)
.NET SDK 10.0.400, runtime 10.0.11, X64 RyuJIT x86-64-v3
```

Sebuah CPU laptop. Perangkat keras server akan jauh lebih baik; yang bertahan lama adalah *rasio*-nya.

## Throughput pesan

`actornet bench -n 2000000 -a 8`, tiga kali jalan:

| Jalan | Drained | Alokasi |
| --- | --- | --- |
| 1 | 3.596.089 pesan/detik | 160 B/pesan |
| 2 | 3.407.405 pesan/detik | 166 B/pesan |
| 3 | 3.166.185 pesan/detik | 181 B/pesan |

**3,2–3,6 juta pesan/detik**, 8 actor, 8 task pengirim, 2 juta pesan. Selisih antar-jalan lebih besar
daripada kebanyakan mikro-optimasi, dan itu perlu diketahui sebelum mengejar salah satunya.

### Kenapa "drained" dan bukan "dispatched"

Benchmark melaporkan keduanya:

```
Dispatch only   3.035.763 pesan/detik  (yang dilaporkan benchmark naif)
Drained         2.981.276 pesan/detik  (setiap pesan ditangani)
```

`TellAsync` selesai saat pesan **diterima ke dalam mailbox**, bukan saat ia ditangani. Karena itu,
mengukur perulangan `tell` hanya mengukur seberapa cepat proses ini mengisi channel — angka besar yang
tidak bermakna, dan itulah yang dilaporkan benchmark naif.

Angka "drained" diakhiri penghalang berupa satu ask per actor. Sebuah ask terurut di belakang semua isi
mailbox, jadi balasannya yang tiba membuktikan antrean di depannya sudah ditangani. Timer atau sleep
tidak bisa memberi jaminan itu.

### Ke mana alokasinya pergi

160–180 byte per pesan terdengar tinggi untuk sebuah "jalur panas". Sebagian besarnya adalah
penyimpanan segmen milik channel tak berbatas itu sendiri: saat pengirim mendahului penangan, jutaan
envelope tertampung, dan sebuah `Envelope` adalah struct dengan enam field. Pada 2 juta pesan itu
lebih dari 100 MB antrean.

Memang **ada** jalur cepat sinkron yang menghindari pembangunan mesin-state async saat mailbox
menerima seketika, yang selalu terjadi bila mailbox tak berbatas. Ia tidak terlihat di benchmark ini
— alokasi pengisian antrean mendominasi dan variansi antar-jalan lebih besar daripada efeknya.
Penghematannya nyata pada beban kerja steady-state, bukan pada beban pengisian antrean.

## Perutean dan serialisasi

BenchmarkDotNet, `RoutingBenchmarks`:

| Operasi | Member | Rata-rata | Alokasi |
| --- | --- | --- | --- |
| Hash satu key | 3 | 39,1 ns | — |
| Hash satu key | 12 | 38,2 ns | — |
| Menempatkan 1.024 key di ring | 3 | 97,8 µs (≈95 ns/key) | — |
| Menempatkan 1.024 key di ring | 12 | 109,4 µs (≈107 ns/key) | — |
| Serialisasi satu pesan | 3 | 631 ns | 288 B |
| Serialisasi dan deserialisasi | 3 | 1.230 ns | 480 B |

Tiga hal yang mengikuti.

**Penempatan hampir gratis dan nyaris tidak tumbuh bersama cluster.** 95 ns pada 3 member, 107 ns pada
12 — ring-nya adalah pencarian biner atas posisi terurut, jadi ukuran cluster berongkos logaritmik.
Tidak ada yang dialokasikan.

**Serialisasi berongkos 6× dari penempatan.** Itulah pengukuran di balik keputusan desainnya: jalur
pengiriman lokal membawa objek pesannya sendiri, dan hanya kabel yang melakukan serialisasi. Framework
yang menyerialisasi secara lokal akan membayar 630 ns dan 288 byte pada setiap pesan di deployment
satu node, tanpa imbalan apa pun.

**Hop remote didominasi serialisasi dan jaringan**, bukan runtime. Bila throughput remote penting,
format biner adalah tuasnya — dan itu ada di [roadmap](../../Plan.md).

## Apa yang tidak ditunjukkan angka-angka ini

Ini adalah mikro-benchmark jalur in-process satu mesin. Secara spesifik ia mengecualikan:

- **Jaringan.** Tidak ada hop remote, tidak ada TCP, tidak ada serialisasi di jalur yang diukur.
- **Persistensi.** Tidak ada penulisan store. `PersistentActor` yang melakukan checkpoint setiap pesan
  dibatasi store-nya, bukan mailbox-nya.
- **Handler nyata.** Actor di benchmark hanya menambah sebuah integer. Actor yang memanggil basis data
  dibatasi basis datanya, dan angka ini tidak relevan baginya.
- **Persaingan dengan hal lain.** Proses benchmark menguasai mesinnya sendirian.
- **Operasi berkelanjutan.** Ini jalan singkat. Belum ada soak test.

Bila Anda sedang menakar sebuah deployment, ukur beban kerja Anda sendiri. Yang berguna di sini adalah
*batas bawahnya*: runtime bukan yang akan membatasi Anda.

## Mereproduksi

```bash
actornet bench -n 2000000 -a 8
dotnet run -c Release --project benchmarks/ActorNet.Benchmarks
dotnet run -c Release --project benchmarks/ActorNet.Benchmarks -- --filter '*RoutingBenchmarks*'
```

Hanya Release — BenchmarkDotNet menolak build debug, dan angka CLI bench dari build debug tidak akan
berarti.

## Penyetelan

**Kapasitas mailbox.** Tak berbatas secara bawaan, dan itulah yang membuat sebuah `tell` murah. Ia
mengubah konsumen lambat menjadi pertumbuhan memori tanpa batas, jadi batasi untuk actor mana pun yang
diberi makan selang pemadam eksternal:

```csharp
options.MailboxCapacity = 10_000;   // pengirim menunggu begitu actor tertinggal sejauh ini
```

**Batas waktu menganggur.** Lebih pendek membebaskan memori lebih cepat dan membayar lebih banyak
aktivasi; lebih panjang menahan lebih banyak actor tetap tinggal. Bila aktivasi ulangnya mahal —
actor event-sourced dengan stream panjang tanpa snapshot — perpanjang.

**Snapshot.** `SnapshotEvery` adalah tuas antara waktu pemulihan dan penulisan journal. Actor
event-sourced yang sibuk tanpa snapshot memutar ulang seluruh riwayatnya pada setiap aktivasi.

**Virtual node.** 128 per member menjaga ring 3 node dalam beberapa persen dari rata. Menaikkannya
memperbaiki pembagian sedikit saja dan memperbesar ring; tidak ada alasan mengubahnya di bawah
beberapa puluh member.

**Server GC menyala** di `Directory.Build.props`. Sebuah node adalah pompa pesan: banyak alokasi kecil
berumur pendek di seluruh core, dan itu persis kasus yang menjadi alasan keberadaan server GC.

## Selanjutnya

- [Arsitektur](02-arsitektur.md) — kenapa jalur lokal tidak melakukan serialisasi
- [Perkakas](09-perkakas.md) — menjalankan benchmark
