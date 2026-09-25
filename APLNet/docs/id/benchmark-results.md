# Hasil benchmark

[English](../en/benchmark-results.md) · [Indeks](README.md)

Halaman ini berisi hasil BenchmarkDotNet untuk matriks di spesifikasi §5, yaitu B1–B7. Laporan
mentahnya, lengkap dengan semua kolom, ada di [docs/benchmarks/](../benchmarks/). Angka ditampilkan
**baik saat APL.Net kalah maupun saat menang**, beserta alasannya.

## Lingkungan (spesifikasi §5.1)

| | |
|---|---|
| CPU | Intel Core i7-8650U, 1,90 GHz (turbo 4,2 GHz), Kaby Lake R: **4 core / 8 thread**, AVX2, tanpa AVX-512 |
| Mesin | Laptop tersambung listrik, power plan Windows "High performance" |
| OS | Windows 11 25H2 (10.0.26200) |
| Runtime | .NET 10.0.12, RyuJIT x86-64-v3, SDK 10.0.401 |
| Harness | BenchmarkDotNet 0.15.8, `Job.Default` di .NET 10, 5 warm-up, 10–30 iterasi, `MemoryDiagnoser` |
| Variabel lingkungan | tidak ada yang disetel (`DOTNET_TieredPGO` dan `DOTNET_ReadyToRun` default) |
| Tanggal | 2026-09-25, total 24 menit untuk 66 benchmark |

Cara mengulang:

```sh
dotnet run -c Release --project benchmarks/APL.Net.Benchmarks -- --filter '*'
```

Tutup aplikasi lain lebih dulu, karena partisi statis sensitif terhadap core yang sedang sibuk.

## Ringkasan terhadap kriteria sukses spesifikasi (§5.3)

| Kriteria | Hasil | Terpenuhi? |
|---|---|---|
| B1/B2: struct invoker ≥ 2× `Parallel.For` pada 1M+ | B1 1M: 0,98×, B1 100M: 0,99× (terikat memori). B2 1M: **1,26×** | **Tidak**, lihat di bawah |
| B1 di dalam cache (10K), sebagai pembanding | **3,9×** (struct), **4,4×** (range struct) | ya |
| B4/B5: SIMD ≥ 3× skalar sekuensial | B4 1M **7,6×**, B5 1M **6,0×**. Pada 10M: 1,2× / 2,6× (terikat memori) | pada 1M |
| B4/B5: SIMD ≥ 1,5× `Parallel.For` | B4 **19×** (1M), **2,2×** (10M). B5 **5,7×** (1M), **2,0×** (10M) | **ya** |
| B6: dokumentasikan saat `Parallel.For` menang | Statis kalah **2,4×** (sorted). Work stealing menang **1,44×** (sorted) dan kalah **1,5×** (shuffled) | terdokumentasi |
| B7: nol alokasi per panggilan struct invoker | **0 B** di semua benchmark struct body | **ya** |

### Mengapa NFR1 (≥ 2× pada 1M+) tidak terpenuhi untuk kerja sederhana

`x * 2 + 1` atas 1M double membaca 8 MB dan menulis 8 MB, kira-kira sebesar cache L3 CPU ini
(8 MB). Pada 100M elemen, jumlahnya 1,6 GB. Batasnya di sini bukan aritmetika: semua varian,
**termasuk loop satu thread**, selesai kira-kira dalam waktu yang dibutuhkan DRAM untuk
memindahkan data. Sekuensial butuh 1,20 ms dan semua varian paralel 1,18–1,41 ms. Tidak ada
scheduler yang bisa mengalahkan bandwidth memori. Saat loop yang sama muat di cache (B1 pada 10K),
overhead dispatch yang dihapus APL.Net menjadi dominan, dan struct body **3,9×** lebih cepat dari
`Parallel.For`.

B2 (`√x · sin x`, ~10 ns per elemen) terikat komputasi, dan APL.Net **1,26×** lebih cepat.
Dispatch delegate hanya sebagian dari biaya per elemen, jadi angka itu adalah porsi dispatch saja.
Untuk mencapai 2× di sini dibutuhkan aritmetika yang lebih murah, bukan scheduler yang lebih baik.

## B1: aritmetika sederhana, `dst[i] = src[i] * 2 + 1`

| Metode | 10K | 1M | 100M | Alokasi |
|---|---:|---:|---:|---:|
| Sekuensial | 5,33 µs | 1.197 µs | 181,9 ms | – |
| `Parallel.For` | 25,44 µs | 1.383 µs | 166,7 ms | ~3,0 KB |
| `Parallel.ForEach(Partitioner.Create)` (TPL ahli) | 14,81 µs | 1.178 µs | 162,5 ms | ~4,5 KB |
| `Apl.For` (lambda) | 15,36 µs | 1.176 µs | 165,9 ms | 96 B (lambda) |
| `Apl.For` (struct) | **6,53 µs** | 1.406 µs | 167,7 ms | **0 B** |
| `Apl.ForRange` (struct) | **5,81 µs** | 1.254 µs | 169,2 ms | **0 B** |

Di dalam cache, loop struct body hanya sedikit lebih mahal dari loop sekuensial ditambah satu
proses membangunkan thread, sedangkan `Parallel.For` **4,8×** lebih mahal dari sekuensial. Di luar
cache, semua varian berada dalam rentang noise satu sama lain.

## B2: komputasi sedang, `√x · sin x`, 1M

| Metode | Rata-rata | vs `Parallel.For` | Alokasi |
|---|---:|---:|---:|
| Sekuensial | 10.627 µs | 0,38× | – |
| `Parallel.For` | 3.986 µs | 1,00× | 3.111 B |
| `Apl.For` (lambda) | 3.760 µs | 1,06× | 96 B |
| `Apl.For` (struct) | **3.166 µs** | **1,26×** | **0 B** |

## B3: komputasi berat, pangkat matriks 4×4 (~880 ns per elemen), 100K

| Metode | Rata-rata | Alokasi |
|---|---:|---:|
| Sekuensial | 88,2 ms | – |
| `Parallel.For` | 31,1 ms | 3.167 B |
| `Apl.For` (lambda) | 32,8 ms | 96 B |
| `Apl.For` (struct) | 31,6 ms | **0 B** |

Sesuai perkiraan, semua varian paralel setara dalam rentang noise. Pada ~1 µs per elemen, biaya
dispatch tidak berpengaruh, dan sekitar 2,8× di 4 core fisik adalah batas yang diberikan
perangkat keras.

## B4: SIMD AXPY, `x = x · 0,5 + 1` atas float

| Metode | 1M | vs skalar | vs `Parallel.For` | 10M |
|---|---:|---:|---:|---:|
| Skalar sekuensial | 458 µs | 1,00× | 2,5× | 6.252 µs |
| `Parallel.For` | 1.138 µs | 0,40× | 1,00× | 11.572 µs |
| `Apl.For` (struct, body skalar) | 332 µs | 1,41× | 3,4× | 5.649 µs |
| `SimdOps.TransformInPlace` (1 thread) | 143 µs | 3,3× | 7,9× | 4.801 µs |
| **`SimdOps.ParallelTransformInPlace`** | **60 µs** | **7,6×** | **19×** | **5.310 µs** |
| `ParallelTransformInPlace` (delegate) | 90 µs | 5,1× | 12,7× | 5.336 µs |

Di sini `Parallel.For` *lebih lambat dari satu thread skalar*. Body-nya terlalu murah untuk menanggung
pemanggilan delegate dan pengecekan loop-state di setiap elemen. Pada 10M float (40 MB), semuanya
kembali terikat DRAM, dan kernel SIMD paralel berakhir di titik yang sama dengan versi satu thread.

## B5: jumlah double

| Metode | 1M | 10M | Alokasi |
|---|---:|---:|---:|
| LINQ `Sum()` | 1.112 µs | 13.116 µs | – |
| Loop sekuensial | 1.099 µs | 13.481 µs | – |
| `Parallel.For` + subtotal per thread | 1.103 µs | 10.151 µs | ~3,5 KB |
| `SimdOps.Sum` (1 thread) | 304 µs | 5.041 µs | – |
| **`SimdOps.ParallelSum`** | **194 µs** | **5.062 µs** | **0 B** |

## B6: beban timpang, biaya berdistribusi Zipf atas 10K item

| Metode | Sorted (berat di depan) | Shuffled (acak) |
|---|---:|---:|
| Sekuensial | 38,6 ms | 44,5 ms |
| `Parallel.For` | 12,9 ms | **10,1 ms** |
| `Apl.For`, statis (default) | 31,3 ms | 13,2 ms |
| `Apl.For`, striped (16) | 17,7 ms | 17,1 ms |
| `Apl.For`, work stealing (grain 1) | **8,9 ms** | 15,0 ms |

Benchmark ini ada untuk memperlihatkan kelemahan secara jujur:

- **Sorted:** partisi statis pertama memegang sekitar 78% pekerjaan, sehingga partisi statis 2,4×
  lebih lambat dari `Parallel.For`. Work stealing memulihkan kerugian itu dan mengalahkan TPL 1,44×.
- **Shuffled:** `Parallel.For` menang melawan semua partitioner APL.Net. Chunk dinamisnya, ditambah
  thread tambahan yang disuntikkan pool selama loop panjang, menyebarkan item mahal yang posisinya
  acak dengan lebih baik daripada work stealing dengan 8 worker dan grain 1. Run ini juga memiliki
  variansi tertinggi (StdDev 1,7–6,9 ms), dan baseline sekuensialnya sendiri bervariasi ±15%.
  Baca kolom shuffled sebagai "TPL menang", bukan sebagai rasio yang presisi.

Untuk beban timpang: ukur dengan data Anda sendiri, dan jika tidak ada partitioner APL.Net yang
menang, **pakai `Parallel.For`**.

## B7: biaya tetap satu panggilan

| Metode | 1.000 elemen | 16.000 elemen | Alokasi |
|---|---:|---:|---:|
| `Parallel.For` | 10,35 µs | 37,14 µs | 2,9–3,0 KB |
| `Apl.For` (lambda) | 3,66 µs (**2,8×**) | 21,93 µs (1,7×) | 96 B (lambda) |
| `Apl.For` (struct) | **2,79 µs (3,7×)** | **9,56 µs (3,9×)** | **0 B, tanpa Gen0** |
| Sekuensial | 0,66 µs | 7,81 µs | – |

Jalur struct **tidak mengalokasi apa pun**. Job, wait handle-nya, dan state partisinya diambil dari
pool per tipe body. Alokasi 96 B pada jalur lambda adalah closure dan delegate milik benchmark itu
sendiri.

## Referensi Rust/Rayon (terpisah, hanya ilustrasi)

Program Rust terpisah (`benchmarks/rust-rayon`, rustc 1.97.1 (8bab26f4f 2026-07-14), Rayon 1.x, Criterion 0.5, `lto = "fat"`)
dijalankan di mesin yang sama pada hari yang sama. **Runtime berbeda, harness berbeda, hanya
ilustrasi.** Angkanya tidak bisa dibandingkan langsung karena cara memulai proses dan metodologi
pengukurannya berbeda. Tidak ada FFI: keduanya tidak saling terhubung.

| Beban | Rust sekuensial | Rayon `par_iter` | APL.Net terbaik | `Parallel.For` |
|---|---:|---:|---:|---:|
| B1 `x*2+1`, 10K | 2,45 µs | 69,5 µs | 5,81 µs | 25,44 µs |
| B1 `x*2+1`, 1M | 2,00 ms | 1,74 ms | 1,18 ms | 1,38 ms |
| B1 `x*2+1`, 100M | 166,8 ms | 165,9 ms | 165,9 ms | 166,7 ms |
| B4 AXPY, 1M float | 300 µs | 159 µs | **60 µs** | 1.138 µs |
| B4 AXPY, 10M float | 5,18 ms | 5,47 ms | 5,31 ms | 11,57 ms |
| B5 jumlah, 1M double | 1,15 ms | 372 µs | **194 µs** | 1.103 µs |
| B5 jumlah, 10M double | 11,4 ms | 5,50 ms | 5,06 ms | 10,15 ms |

Yang tampak, dengan catatan di atas:

- Untuk loop paralel-data, APL.Net setara dengan Rayon. APL.Net unggul di tempat SIMD eksplisit
  berperan: sum float di Rust tidak divektorisasi, karena menyusun ulang urutan penjumlahan
  floating-point tidak diizinkan secara default, alasan yang sama mengapa JIT .NET juga tidak
  melakukannya.
- Pada 10K elemen, pembagian work-stealing Rayon jauh lebih mahal daripada hematnya. rustc juga
  memvektorisasi loop sekuensial secara otomatis, sehingga Rust sekuensial mengalahkan semuanya
  di ukuran itu.
- Pada 100M, ketiganya terikat DRAM dan hasilnya berselisih kurang dari 1%.

Cara mengulang: `cargo bench --manifest-path benchmarks/rust-rayon/Cargo.toml`.

## Cara membaca angka ini

- Satu mesin, satu kali run. Di mesin dengan lebih banyak core, penghematan dispatch makin besar
  (penghitung chunk bersama milik TPL makin berebut), dan begitu pula manfaat memilih penyeimbangan
  beban.
- CPU laptop mengubah kecepatan clock sesuai suhu. Simpangan baku ditampilkan; rasio di bawah
  ~1,1× masih dalam rentang noise.
- Galeri menampilkan perbandingan yang sama secara interaktif di mesin *Anda*. Harness di dalam
  aplikasinya lebih cepat, tetapi lebih banyak noise dibanding BenchmarkDotNet.
