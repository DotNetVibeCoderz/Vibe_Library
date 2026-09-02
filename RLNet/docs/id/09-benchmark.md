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

## Jaringannya sendiri

Forward, forward+backward, dan gradient step lengkap termasuk optimizer:

| Batch | Lebar | Forward | + backward | + optimizer | Dialokasikan |
|---:|---:|---:|---:|---:|---:|
| 1 | 64 | 1,3 µs | 3,9 µs | 63,0 µs | 0 B |
| 1 | 256 | 8,2 µs | 36,4 µs | 283,1 µs | 0 B |
| 64 | 64 | 123,5 µs | 313,7 µs | 313,2 µs | 0 B |
| 64 | 256 | 797,5 µs | 2.290,5 µs | 2.621,3 µs | 0 B |
| 256 | 64 | 513,8 µs | 1.284,3 µs | 1.262,5 µs | 0 B |
| 256 | 256 | 2.951,2 µs | 9.396,4 µs | 9.676,8 µs | 0 B |

**Nol byte dialokasikan, di setiap konfigurasi.** Inilah tabel yang mendukung klaim itu; benchmark
agen tidak bisa menunjukkannya karena membangun replay buffer di dalam pengukurannya.

Backward berbiaya sekitar 2,5–3× forward, dan itu rasio yang diharapkan: ia menghitung dua perkalian
(gradien terhadap input, dan terhadap weight) sementara forward menghitung satu.

### Biaya Adam tidak menskala dengan ukuran batch

Bandingkan baris batch-1 dengan baris batch-64. Pada batch 64 optimizernya nyaris gratis — 313,7
melawan 313,2 µs, tak terbedakan. Pada batch 1 dengan jaringan selebar 256, jaringannya 36 µs
melawan total 283 µs: **optimizernya 87% dari langkah itu.**

Itu melekat pada Adam, bukan cacat. Ia menyentuh setiap parameter di jaringan berapa pun
minibatch-nya, jadi biayanya tetap sementara forward dan backward mengecil bersama batch.
Konsekuensi praktisnya: batch yang sangat kecil tidak efisien karena alasan yang sama sekali tidak
berkaitan dengan jaringannya.

Itu juga sebabnya `AdamOptimizer.Apply` divektorkan alih-alih memakai loop skalar yang lebih jelas.
Perubahan itu terukur:

| | Adam skalar | Tervektorkan | |
|---|---:|---:|---|
| Langkah optimizer, batch 1, lebar 64 | 290,5 µs | 59,1 µs | **4,9× lebih cepat** |
| Langkah optimizer, batch 1, lebar 256 | 1.924,4 µs | 246,7 µs | **7,8× lebih cepat** |
| Gradient step penuh, batch 1, lebar 256 | 1.969,5 µs | 283,1 µs | **7,0× lebih cepat** |

Pada batch 64 perubahan yang sama bernilai sekitar 10%, dan pada 256 ia lenyap dalam derau — tapi
rollout pendek A2C dan pembaruan online satu langkah apa pun berada tepat di rentang yang penting.

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
sekitar 1,1 ms, jadi tambahan ~120 µs itu kira-kira 11% — dan pada masalah ber-reward jarang seperti
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
| Q-learning / GridWorld | 5,5 ms | 365.000 |
| A2C / CartPole | 43,4 ms | 46.100 |
| PPO / CartPole | 114,6 ms | 17.450 |
| DQN, uniform replay / CartPole | 2,08 s | 963 |
| DQN / CartPole | 2,13 s | 941 |
| TD3 / Pendulum | 56,8 s | 35 |
| SAC / Pendulum | 99,5 s | 20 |

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

Kedua baris DQN berselisih 2,3%, dengan prioritised yang lebih lambat — arah yang benar, dan
kira-kira sebesar yang diprediksi tabel replay di atas begitu biaya samplingnya dibandingkan dengan
gradient step ~1 ms.

### Soal membandingkan angka ini antar-run

Tabel ini diukur ulang setelah pekerjaan Adam, dan sebagian besar barisnya bergerak 10–44%.
**Hampir tidak ada yang bisa dikaitkan dengan perubahan itu**, dan alasannya perlu disebutkan
alih-alih mengklaim perbaikannya.

Q-learning adalah kontrolnya: ia tidak memakai neural network maupun optimizer sama sekali, jadi
penulisan ulang Adam mustahil menyentuhnya — dan ia tetap bergerak 27%. SAC bergerak 12% ke arah
yang *salah*, yang juga mustahil disebabkan perubahan itu. Keduanya menunjuk hal yang sama: ini
laptop yang sudah menjalankan benchmark berjam-jam tanpa henti, dan variansi antar-run pada tiga
iterasi lebih besar daripada efek yang sedang dicari.

Pengukuran bersih atas perubahan Adam ada di [tabel jaringan](#jaringannya-sendiri), yang
mengisolasi operasinya alih-alih menguburnya di dalam loop training. Pada ukuran batch yang dipakai
agen-agen ini (32–256), tabel itu memprediksi 10% atau kurang — persis rezim tempat pengukuran di
level agen tidak sanggup memisahkannya.

Perlakukan angka di sini sebagai *mesin ini, sore ini* — berguna untuk melihat bentuk peringkat
antar algoritma, yang stabil dan sangat besar selisihnya, dan bukan untuk perbandingan 20% antar-run.

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

## GPU

Diukur pada Intel UHD 620 terintegrasi di mesin ini lewat OpenCL, pada batch 256 — GPU-nya tidak
pernah menang di sini, tapi jaraknya menyempit mantap seiring melebarnya jaringan. Tabel lengkapnya
dan artinya untuk kartu diskret ada di [halaman GPU](06-gpu.md#baca-ini-dulu).

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
