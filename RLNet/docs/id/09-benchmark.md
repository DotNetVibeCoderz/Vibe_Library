# Benchmark

[← Daftar isi](README.md) · [English](../en/09-benchmarks.md)

Setiap angka di sini diukur dengan BenchmarkDotNet pada mesin di bawah. Ulangi dengan:

```bash
dotnet run -c Release --project benchmarks/RLNet.Benchmarks
dotnet run -c Release --project benchmarks/RLNet.Benchmarks -- --filter '*SimdBenchmarks*'
```

**Mesin uji:** .NET 10.0.11, X64 RyuJIT, AVX2 (vektor 256-bit), Server GC, laptop Windows 11. Angka
absolutnya akan berbeda di perangkat keras Anda; rasionya seharusnya tidak.

## Primitif SIMD

Tiga operasi tempat sebuah layer dense terurai, dibandingkan loop yang sama ditulis skalar.

| Operasi | Panjang 256 | Panjang 4.096 | Panjang 65.536 |
|---|---:|---:|---:|
| Dot (skalar) | 343,5 ns | 4.738,8 ns | 76.813,5 ns |
| **Dot (SIMD)** | **41,8 ns** | **590,7 ns** | **12.687,0 ns** |
| *percepatan* | *8,2×* | *8,0×* | *6,1×* |
| AddScaled (skalar) | 426,5 ns | 5.900,7 ns | 98.121,7 ns |
| **AddScaled (SIMD)** | **44,4 ns** | **624,5 ns** | **15.452,9 ns** |
| *percepatan* | *9,6×* | *9,4×* | *6,3×* |
| Polyak blend (SIMD) | 41,8 ns | 649,2 ns | 15.387,8 ns |

Semuanya nol alokasi.

Delapan float per vektor AVX2, jadi 8× adalah langit-langit teoretisnya dan 8–9,6× praktis sudah
menyentuhnya — loop skalarnya tidak divektorkan otomatis oleh JIT di sini. Penurunan ke ~6× pada
65.536 elemen adalah working set-nya keluar dari L2: pada ukuran itu operasinya terbatas memori dan
throughput aritmetika sebesar apa pun tidak menolong.

Itulah sebabnya lebar jaringan lebih penting daripada kelihatannya. Baris layer selebar 256 muat di
cache dan berjalan pada kecepatan SIMD penuh; baris layer selebar 2.048 tidak.

## Simulasi

Melangkah murni, tanpa agen — langit-langit yang dihadapi setiap algoritma. 10.000 langkah per
pengukuran:

| Simulasi | Per 10.000 langkah | Langkah/detik | Dialokasikan |
|---|---:|---:|---:|
| LunarLander | 294,5 µs | 34,0 J | 0 B |
| CartPole | 474,3 µs | 21,1 J | 0 B |
| GridWorld | 575,0 µs | 17,4 J | 0 B |
| Pendulum | 1.021,0 µs | 9,8 J | 0 B |

Puluhan juta langkah per detik, tanpa mengalokasikan apa pun. Environment tidak pernah menjadi
hambatan — itulah inti [kontrak observasi](02-arsitektur.md#kontrak-observasi). Kalau suatu saat ia
jadi hambatan, jawabannya adalah environment tervektorkan, bukan layer yang lebih cepat.

GridWorld lebih lambat daripada CartPole meski lebih sederhana, karena observasinya 25 float one-hot
melawan 4 milik CartPole — pembersihannya mengalahkan fisikanya. Pendulum paling lambat di antara
keempatnya karena `Math.Sin`, `Math.Cos`, dan normalisasi sudutnya adalah panggilan transendental
pada setiap langkah.

## Replay buffer

256 transisi diambil dari buffer berisi **1.000.000 entri**:

| Operasi | Waktu | Rasio | Dialokasikan |
|---|---:|---:|---:|
| Sampel uniform | 22,6 µs | 1,00 | 0 B |
| Sampel prioritised | 83,4 µs | 3,69 | 0 B |
| Sampel prioritised + pembaruan prioritas | 143,7 µs | 6,36 | 0 B |

Prioritised replay berbiaya sekitar 6× uniform untuk satu perjalanan penuh, dan keduanya tidak
mengalokasikan apa pun.

Baca itu terhadap biaya satu gradient step, bukan sendirian. Satu gradient step DQN di mesin ini
sekitar 1,3 ms, jadi tambahan ~120 µs itu kira-kira 9% — dan pada masalah ber-reward jarang seperti
MountainCar, prioritised replay sering kali menjadi pembeda antara belajar dan tidak belajar sama
sekali. Nyaris selalu sepadan.

`SumTree` inilah yang membuatnya terjangkau. Alternatif naifnya — membangun distribusi kumulatif lalu
mencarinya secara biner — adalah O(n) per pengambilan terhadap sejuta entri, pada seluruh 256
pengambilan, pada setiap gradient step. Pohonnya mengubah itu menjadi sekitar dua puluh
perbandingan, dan melacak minimumnya dalam penelusuran yang sama sehingga bobot importance-sampling
juga tidak butuh pemindaian.

## Loop training lengkap

Melangkah simulasi, memilih aksi, menyangga, dan gradient step sekaligus — angka yang benar-benar
dirasakan pengguna. 2.000 langkah per pengukuran, pada **setelan default library**:

| Algoritma / simulasi | Per 2.000 langkah | Langkah/detik |
|---|---:|---:|
| Q-learning / GridWorld | 7,5 ms | 267.000 |
| A2C / CartPole | 54,5 ms | 36.700 |
| PPO / CartPole | 206,4 ms | 9.690 |
| DQN / CartPole | 2,60 s | 770 |
| DQN, uniform replay / CartPole | 2,76 s | 725 |
| TD3 / Pendulum | 63,3 s | 32 |
| SAC / Pendulum | 88,9 s | 22 |

Empat orde besaran perbedaan, dan sebabnya adalah apa yang dilakukan masing-masing per langkah
simulasi:

- **Q-learning** melakukan satu pencarian dictionary dan satu pembaruan aritmetika. Tanpa jaringan
  sama sekali.
- **A2C** menjalankan satu forward pass kecil per langkah, dan satu gradient step per 32 langkah.
- **PPO** mengumpulkan 2.048 langkah, lalu melakukan 10 epoch atasnya dalam minibatch 64 sampel.
- **DQN** melakukan **satu gradient step penuh pada setiap langkah simulasi** secara default,
  termasuk satu forward pass jaringan target dan satu sampel prioritised.
- **SAC dan TD3** melakukan hal yang sama, tapi satu pembaruan menyentuh satu actor *dan empat
  critic* berukuran 256×256, dengan batch 256.

Kedua baris DQN berada dalam rentang derau satu sama lain pada tiga iterasi — biaya sampling
prioritised itu nyata tapi kecil dibanding gradient step-nya, persis seperti yang diprediksi tabel
replay di atas.

### Kalau ini terlalu lambat

`TrainFrequency` adalah pengungkit tunggal terbesar. Satu gradient step jauh lebih mahal daripada
satu langkah simulasi, jadi satu pembaruan per 4 langkah kira-kira melipatempatkan throughput:

```csharp
new SacOptions { TrainFrequency = 4 }        // ~4× langkah per detik
```

Lalu ukuran jaringan dan batch. [Preset demo](08-konsol.md) konsol memakai `[64, 64]` dengan batch 64
dan `TrainFrequency = 2` — kira-kira **sepuluh kali** throughput default SAC, dan masih cukup untuk
mempelajari Pendulum. Itulah beda antara menonton agen belajar dan menonton gambar diam.

Default yang dipublikasikan disetel untuk hasil akhir terbaik pada run panjang, yang merupakan
sasaran tepat untuk pekerjaan training sungguhan dan sasaran keliru untuk sebuah demonstrasi.

## Alokasi

Kolom `Allocated` pada benchmark agen **bukan** nol, dan itu memang diharapkan: benchmark itu
membangun agennya di dalam metode yang diukur, jadi angkanya adalah replay buffer dan jaringannya,
dialokasikan sekali. DQN dengan buffer 100.000 entri atas observasi 4 dimensi mencadangkan sekitar
4 MB hanya untuk buffernya.

Alokasi kondisi tunak adalah nol, dan itulah yang ditunjukkan tabel SIMD, simulasi, dan replay di
atas — semuanya `0 B` melintasi jutaan operasi. `NeuralBenchmarks` mengisolasi siklus
forward-backward-update dengan alasan yang sama.

Memori buffer bisa diperkirakan:

```
byte ≈ kapasitas × (2 × observationSize + actionSize + 2) × 4
```

Satu juta transisi atas observasi 8 dimensi kira-kira 76 MB.

## Mengulanginya

```bash
# semuanya (sekitar 40 menit; suite agen mendominasi)
dotnet run -c Release --project benchmarks/RLNet.Benchmarks

# satu suite
dotnet run -c Release --project benchmarks/RLNet.Benchmarks -- --filter '*SimdBenchmarks*'
dotnet run -c Release --project benchmarks/RLNet.Benchmarks -- --filter '*ReplayBenchmarks*'
dotnet run -c Release --project benchmarks/RLNet.Benchmarks -- --filter '*GpuBenchmarks*'

# akselerator apa yang terlihat
dotnet run -c Release --project benchmarks/RLNet.Benchmarks -- --devices
```

`AgentBenchmarks` menjalankan job pendek — satu pemanasan, tiga iterasi — karena satu iterasi di
sini adalah ribuan gradient step sungguhan, bukan mikrobenchmark. Run default akan memakan satu jam,
dan sebaran antar-run pada sesuatu sepanjang itu sudah cukup kecil sehingga iterasi tambahan tidak
membeli presisi yang sepadan dengan waktunya. Baris DQN di atas adalah peringatannya: pada tiga
iterasi, selisih 6% tidak bisa dibedakan dari derau.

Konfigurasi Release itu wajib. BenchmarkDotNet menolak menjalankan build Debug, dan itu benar.

## Selanjutnya

- [Mesin neural](05-neural-network.md) — kenapa angka SIMD-nya begitu
- [GPU](06-gpu.md) — di mana titik impas akseleratornya
- [Pemecahan masalah](11-pemecahan-masalah.md) — kalau angka Anda jauh dari ini
